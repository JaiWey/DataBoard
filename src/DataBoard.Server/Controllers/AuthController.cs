using DataBoard.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DataBoard.Server.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(IIdentityService identity, IWebHostEnvironment env) : ControllerBase
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
