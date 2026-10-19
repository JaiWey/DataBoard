using System.Security.Cryptography;
using System.Text;
using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Users;

namespace DataBoard.Application.ApiTokens;

/// <summary>
/// How long a new token lasts: a number of days (1 to 365), or <paramref name="NeverExpires"/>. One of the two has to be
/// said, so a token is never valid forever by leaving a field out.
/// </summary>
public sealed record CreateApiTokenRequest(string Name, ApiTokenScope Scope, int? ExpiresInDays = null, bool NeverExpires = false);

public sealed record ApiTokenDto(Guid Id, string Name, ApiTokenScope Scope, DateTime CreatedUtc, DateTime? ExpiresUtc, DateTime? LastUsedUtc, bool Expired);

/// <param name="Token">The secret. Shown once, here: only its hash is kept.</param>
public sealed record ApiTokenCreatedDto(Guid Id, string Name, ApiTokenScope Scope, string Token, DateTime CreatedUtc, DateTime? ExpiresUtc);

/// <summary>Who a valid token acts as, and what it may do.</summary>
public sealed record ApiTokenIdentity(Guid UserId, ApiTokenScope Scope, string Name);

public static class TokenSecrets
{
    /// <summary>A recognisable start, so a token that ends up in a repository is easy to find and revoke.</summary>
    public const string Prefix = "dbp_";

    private const int SecretBytes = 32; // 256 bits: cannot be guessed
    private const int SecretLength = 43; // the same bytes as unpadded base64url

    public static string NewToken() =>
        Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>A cheap check before touching the database: anything not shaped like one of our tokens cannot be one.</summary>
    public static bool LooksValid(string? token) =>
        token is not null
        && token.Length == Prefix.Length + SecretLength
        && token.StartsWith(Prefix, StringComparison.Ordinal)
        && token.Skip(Prefix.Length).All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

public sealed class CreateApiTokenHandler(IApiTokenRepository tokens, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<ApiTokenCreatedDto> HandleAsync(CreateApiTokenRequest request, CancellationToken ct = default)
    {
        var userId = access.RequireUserId();
        access.RequireNotGuest();
        var now = clock.UtcNow;

        DateTime? expires = null;
        if (!request.NeverExpires)
        {
            if (request.ExpiresInDays is not { } days || days < 1 || days > ApiToken.MaxLifetimeDays)
                throw new ValidationException("ExpiresInDays", $"Choose how long the token lasts: 1 to {ApiToken.MaxLifetimeDays} days, or never.");
            expires = now.AddDays(days);
        }

        if (await tokens.CountForUserAsync(userId, ct) >= ApiToken.MaxPerUser)
            throw new ValidationException("Tokens", $"You can have at most {ApiToken.MaxPerUser} tokens. Revoke one first.");

        var secret = TokenSecrets.NewToken();
        var token = DatasetQuery.Guard(() => ApiToken.Create(userId, request.Name ?? string.Empty, TokenSecrets.Hash(secret), request.Scope, now, expires));

        await tokens.AddAsync(token, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return new ApiTokenCreatedDto(token.Id, token.Name, token.Scope, secret, token.CreatedUtc, token.ExpiresUtc);
    }
}

public sealed class ListApiTokensHandler(IApiTokenRepository tokens, IClock clock, OrganizationAccess access)
{
    public async Task<IReadOnlyList<ApiTokenDto>> HandleAsync(CancellationToken ct = default)
    {
        var userId = access.RequireUserId();
        var now = clock.UtcNow;
        return (await tokens.ListForUserAsync(userId, ct))
            .Select(t => new ApiTokenDto(t.Id, t.Name, t.Scope, t.CreatedUtc, t.ExpiresUtc, t.LastUsedUtc, !t.IsActive(now)))
            .ToList();
    }
}

public sealed class RevokeApiTokenHandler(IApiTokenRepository tokens, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task HandleAsync(Guid tokenId, CancellationToken ct = default)
    {
        var userId = access.RequireUserId();
        var token = await tokens.GetAsync(userId, tokenId, ct) ?? throw new NotFoundException("Token not found.");
        tokens.Remove(token);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>Turns the secret presented by a client into the user it acts as. Anything wrong gives null.</summary>
public sealed class AuthenticateApiTokenHandler(IApiTokenRepository tokens, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<ApiTokenIdentity?> HandleAsync(string? secret, CancellationToken ct = default)
    {
        if (!TokenSecrets.LooksValid(secret)) return null;

        var token = await tokens.FindByHashAsync(TokenSecrets.Hash(secret!), ct);
        var now = clock.UtcNow;
        if (token is null || !token.IsActive(now)) return null;

        if (token.MarkUsed(now)) await unitOfWork.SaveChangesAsync(ct);
        return new ApiTokenIdentity(token.UserId, token.Scope, token.Name);
    }
}
