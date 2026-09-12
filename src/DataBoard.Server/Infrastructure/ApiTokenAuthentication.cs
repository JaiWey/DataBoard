using System.Security.Claims;
using System.Text.Encodings.Web;
using DataBoard.Application.ApiTokens;
using DataBoard.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DataBoard.Server.Infrastructure;

public static class ApiTokenDefaults
{
    public const string Scheme = "ApiToken";

    /// <summary>The claim that says what the token may do; the MCP tools check it before changing anything.</summary>
    public const string ScopeClaim = "token_scope";
}

/// <summary>
/// Accepts a personal access token ("Authorization: Bearer dbp_...") and signs the request in as the token's owner,
/// so every handler applies the owner's organization roles exactly as it would for the owner. This scheme is attached
/// to the MCP endpoint only: a token is not accepted anywhere else in the API, so it cannot manage tokens or the account.
/// </summary>
public sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

        var identity = await Context.RequestServices.GetRequiredService<AuthenticateApiTokenHandler>()
            .HandleAsync(header["Bearer ".Length..].Trim(), Context.RequestAborted);
        if (identity is null) return AuthenticateResult.Fail("The token is not valid, has expired or was revoked.");

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, identity.UserId.ToString()),
                new Claim(ApiTokenDefaults.ScopeClaim, identity.Scope.ToString()),
                new Claim("token_name", identity.Name),
            ],
            Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

public static class ApiTokenClaims
{
    public static ApiTokenScope? ScopeOf(ClaimsPrincipal user) =>
        Enum.TryParse<ApiTokenScope>(user.FindFirst(ApiTokenDefaults.ScopeClaim)?.Value, out var scope) ? scope : null;
}
