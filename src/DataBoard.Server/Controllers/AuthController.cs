using DataBoard.Application.Identity;
using System.Security.Claims;
using DataBoard.Infrastructure.Identity;
using DataBoard.Server.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DataBoard.Server.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(IIdentityService identity, IWebHostEnvironment env, IOptions<GitHubOptions> github, IOptions<GuestOptions> guests) : ControllerBase
{
	public const string RefreshCookieName = "databoard_refresh";

	public sealed record RegisterBody(string Email, string Password, string DisplayName);
	public sealed record LoginBody(string Email, string Password);

	/// <summary>The refresh token is sent only as an HttpOnly cookie, never in the body, so scripts cannot read it.</summary>
	public sealed record SessionResponse(string AccessToken, DateTime AccessTokenExpiresUtc, UserInfo User);

	[HttpPost("register")]
	[AllowAnonymous]
	public async Task<ActionResult<UserInfo>> Register(RegisterBody body, CancellationToken ct)
	{
		var user = await identity.RegisterAsync(body.Email, body.Password, body.DisplayName, ct);
		return Created("api/auth/me", user);
	}

	[HttpPost("login")]
	[AllowAnonymous]
	public async Task<ActionResult<SessionResponse>> Login(LoginBody body, CancellationToken ct)
	{
		var session = await identity.LoginAsync(body.Email, body.Password, ct);
		SetRefreshCookie(session);
		return ToResponse(session);
	}

	/// <summary>"Continue as guest": a temporary account with a sandbox of its own, deleted after a day.</summary>
	[HttpPost("guest")]
	[AllowAnonymous]
	[EnableRateLimiting("guest")]
	public async Task<ActionResult<SessionResponse>> Guest(
		[FromServices] Application.Organizations.SeedGuestSandboxHandler sandbox, [FromServices] ILogger<AuthController> logger, CancellationToken ct)
	{
		if (!guests.Value.Enabled) return NotFound();
		var session = await identity.StartGuestAsync(ct);

		// The example is made as the new guest, through the handlers a signed-in guest would use. If it cannot be made the guest
		// still gets an empty sandbox, which is the lesser problem.
		HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
			[new Claim(JwtRegisteredClaimNames.Sub, session.User.Id.ToString()), new Claim(CurrentUser.GuestClaim, "true")], "guest"));
		try
		{
			await sandbox.HandleAsync(ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Could not put the example into a guest sandbox.");
		}

		SetRefreshCookie(session);
		return ToResponse(session);
	}

	public sealed record ProvidersResponse(bool GitHub, bool Guest);

	/// <summary>Which ways of signing in besides a password are set up, so the sign-in page offers only those.</summary>
	[HttpGet("providers")]
	[AllowAnonymous]
	public ActionResult<ProvidersResponse> Providers() => new ProvidersResponse(github.Value.Enabled, guests.Value.Enabled);

	/// <summary>Sends the browser to GitHub. It comes back to <c>github/callback</c> and then to <see cref="GitHubComplete"/>.</summary>
	[HttpGet("github")]
	[AllowAnonymous]
	public IActionResult GitHub([FromQuery] string? returnUrl)
	{
		if (!github.Value.Enabled) return NotFound();

		var complete = "/api/auth/github/complete";
		if (LocalPath(returnUrl) is { } path) complete += "?returnUrl=" + Uri.EscapeDataString(path);
		return Challenge(new AuthenticationProperties { RedirectUri = complete }, GitHubSignIn.Scheme);
	}

	/// <summary>
	/// The last step: turns what GitHub said into a DataBoard sign-in (the same refresh cookie a password sign-in sets) and
	/// goes back to the app, which picks the session up through <c>refresh</c>. Whatever goes wrong ends at the sign-in page.
	/// </summary>
	[HttpGet("github/complete")]
	[AllowAnonymous]
	public async Task<IActionResult> GitHubComplete([FromQuery] string? returnUrl, CancellationToken ct)
	{
		var result = await HttpContext.AuthenticateAsync(GitHubSignIn.ExternalScheme);
		await HttpContext.SignOutAsync(GitHubSignIn.ExternalScheme); // the answer is used once
		if (!github.Value.Enabled || result.Principal is not { } who) return LocalRedirect("/login?error=github");

		var key = who.FindFirstValue(ClaimTypes.NameIdentifier);
		var email = who.FindFirstValue(GitHubSignIn.EmailClaim);
		if (string.IsNullOrEmpty(key)) return LocalRedirect("/login?error=github");
		if (string.IsNullOrEmpty(email)) return LocalRedirect("/login?error=github-email");

		var name = who.FindFirstValue(GitHubSignIn.NameClaim) ?? who.FindFirstValue(GitHubSignIn.LoginClaim);
		try
		{
			var session = await identity.SignInExternalAsync(new ExternalIdentity("GitHub", key, email, EmailVerified: true, name), ct);
			SetRefreshCookie(session);
		}
		catch (Application.Common.UnauthorizedException)
		{
			return LocalRedirect("/login?error=github");
		}
		return LocalRedirect(LocalPath(returnUrl) ?? "/");
	}

	/// <summary>A path on this site and nothing else, so a sign-in link cannot be made to send people elsewhere.</summary>
	private static string? LocalPath(string? url) =>
		!string.IsNullOrEmpty(url) && url.Length < 500 && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal)
			&& !url.Contains('\\') && !url.Any(char.IsControl) ? url : null;

	[HttpPost("refresh")]
	[AllowAnonymous]
	public async Task<ActionResult<SessionResponse>> Refresh(CancellationToken ct)
	{
		var token = Request.Cookies[RefreshCookieName];
		try
		{
			var session = await identity.RefreshAsync(token ?? string.Empty, ct);
			SetRefreshCookie(session);
			return ToResponse(session);
		}
		catch (Application.Common.UnauthorizedException)
		{
			ClearRefreshCookie();
			throw;
		}
	}

	[HttpPost("logout")]
	[AllowAnonymous]
	public async Task<IActionResult> Logout(CancellationToken ct)
	{
		await identity.LogoutAsync(Request.Cookies[RefreshCookieName] ?? string.Empty, ct);
		ClearRefreshCookie();
		return NoContent();
	}

	[HttpGet("me")]
	[Authorize]
	public async Task<ActionResult<UserInfo>> Me(CancellationToken ct)
	{
		var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
		if (!Guid.TryParse(sub, out var id)) return Unauthorized();

		var user = await identity.GetUserAsync(id, ct);
		return user is null ? Unauthorized() : Ok(user);
	}

	private static ActionResult<SessionResponse> ToResponse(AuthSession s) =>
		new SessionResponse(s.AccessToken, s.AccessTokenExpiresUtc, s.User);

	private CookieOptions CookieOptions(DateTime? expiresUtc) => new()
	{
		HttpOnly = true,
		// Cookies are marked Secure everywhere except plain-HTTP local development.
		Secure = !env.IsDevelopment() || Request.IsHttps,
		SameSite = SameSiteMode.Strict,
		Path = "/api/auth",
		Expires = expiresUtc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(expiresUtc.Value, DateTimeKind.Utc)),
	};

	private void SetRefreshCookie(AuthSession s) =>
		Response.Cookies.Append(RefreshCookieName, s.RefreshToken, CookieOptions(s.RefreshTokenExpiresUtc));

	private void ClearRefreshCookie() =>
		Response.Cookies.Delete(RefreshCookieName, CookieOptions(null));
}
