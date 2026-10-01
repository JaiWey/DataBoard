using System.Security.Cryptography;
using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Identity;
using DataBoard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DataBoard.Infrastructure.Identity;

internal sealed class IdentityService(
    UserManager<AppUser> users,
    AppDbContext db,
    JwtTokenService tokens,
    IOptions<JwtOptions> jwtOptions,
    IClock clock) : IIdentityService
{
    private const string InvalidCredentials = "Invalid email or password.";

    // Verified against when the email is unknown, so unknown and known emails take similar time.
    private static readonly Lazy<string> DummyHash =
        new(() => new PasswordHasher<AppUser>().HashPassword(new AppUser(), Guid.NewGuid().ToString("N")));

    public async Task<UserInfo> RegisterAsync(string email, string password, string displayName, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, List<string>>();

        email = (email ?? string.Empty).Trim();
        displayName = (displayName ?? string.Empty).Trim();

        if (email.Length == 0 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            Add(errors, "Email", "A valid email address is required.");
        if (displayName.Length == 0)
            Add(errors, "DisplayName", "Display name is required.");
        else if (displayName.Length > AppUser.MaxDisplayNameLength)
            Add(errors, "DisplayName", $"Display name must be at most {AppUser.MaxDisplayNameLength} characters.");
        if (string.IsNullOrEmpty(password))
            Add(errors, "Password", "Password is required.");

        if (errors.Count > 0) throw ToValidation(errors);

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            CreatedUtc = clock.UtcNow,
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                var field = error.Code.StartsWith("Password", StringComparison.Ordinal) ? "Password" : "Email";
                var message = error.Code is "DuplicateUserName" or "DuplicateEmail"
                    ? "That email is already registered."
                    : error.Description;
                Add(errors, field, message);
            }
            throw ToValidation(errors);
        }

        return ToInfo(user);
    }

    public async Task<AuthSession> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync((email ?? string.Empty).Trim());

        if (user is null)
        {
            new PasswordHasher<AppUser>().VerifyHashedPassword(new AppUser(), DummyHash.Value, password ?? string.Empty);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (await users.IsLockedOutAsync(user))
            throw new UnauthorizedException(InvalidCredentials);

        if (!await users.CheckPasswordAsync(user, password ?? string.Empty))
        {
            await users.AccessFailedAsync(user);
            throw new UnauthorizedException(InvalidCredentials);
        }

        await users.ResetAccessFailedCountAsync(user);
        return await IssueSessionAsync(user, ct);
    }

    public async Task<AuthSession> SignInExternalAsync(ExternalIdentity identity, CancellationToken ct = default)
    {
        const string Refused = "Could not sign in with that account.";

        var known = await users.FindByLoginAsync(identity.Provider, identity.ProviderKey);
        if (known is not null)
        {
            if (await users.IsLockedOutAsync(known)) throw new UnauthorizedException(Refused);
            return await IssueSessionAsync(known, ct);
        }

        // Someone new to this service: the email is all there is to go on, and only a verified one counts.
        var email = (identity.Email ?? string.Empty).Trim();
        if (!identity.EmailVerified || email.Length == 0)
            throw new UnauthorizedException($"{identity.Provider} did not share a verified email address.");

        var login = new UserLoginInfo(identity.Provider, identity.ProviderKey, identity.Provider);
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (await users.IsLockedOutAsync(existing)) throw new UnauthorizedException(Refused);
            if (!(await users.AddLoginAsync(existing, login)).Succeeded) throw new UnauthorizedException(Refused);

            // Registering never checked the email, so anyone could have made this account with a password they know,
            // in the hope that its real owner comes along. Whoever proved the email now owns it: the password stops
            // working and every other sign-in ends.
            if (!existing.EmailConfirmed)
            {
                existing.EmailConfirmed = true;
                existing.PasswordHash = null;
                existing.SecurityStamp = Guid.NewGuid().ToString("N");
                if (!(await users.UpdateAsync(existing)).Succeeded) throw new UnauthorizedException(Refused);
                await RevokeAllAsync(existing.Id, clock.UtcNow, ct);
            }
            return await IssueSessionAsync(existing, ct);
        }

        var name = (identity.DisplayName ?? string.Empty).Trim();
        if (name.Length == 0) name = email.Split('@')[0];
        if (name.Length > AppUser.MaxDisplayNameLength) name = name[..AppUser.MaxDisplayNameLength];

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name, CreatedUtc = clock.UtcNow };
        if (!(await users.CreateAsync(user)).Succeeded) throw new UnauthorizedException(Refused);
        if (!(await users.AddLoginAsync(user, login)).Succeeded)
        {
            await users.DeleteAsync(user);
            throw new UnauthorizedException(Refused);
        }
        return await IssueSessionAsync(user, ct);
    }

    public async Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedException("Invalid refresh token.");

        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedException("Invalid refresh token.");

        var now = clock.UtcNow;

        if (stored.RevokedUtc is not null)
        {
            // A rotated token was presented again: assume theft and end every session of this user.
            await RevokeAllAsync(stored.UserId, now, ct);
            throw new UnauthorizedException("Invalid refresh token.");
        }

        if (stored.ExpiresUtc <= now)
            throw new UnauthorizedException("Invalid refresh token.");

        var user = await users.FindByIdAsync(stored.UserId.ToString())
            ?? throw new UnauthorizedException("Invalid refresh token.");

        stored.RevokedUtc = now;
        return await IssueSessionAsync(user, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is { RevokedUtc: null })
        {
            stored.RevokedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<UserInfo?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : ToInfo(user);
    }

    public async Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync((email ?? string.Empty).Trim());
        return user?.Id;
    }

    private async Task<AuthSession> IssueSessionAsync(AppUser user, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var (accessToken, accessExpires) = tokens.CreateAccessToken(user);

        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        var refreshExpires = now.AddDays(jwtOptions.Value.RefreshTokenDays);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(raw),
            CreatedUtc = now,
            ExpiresUtc = refreshExpires,
        });
        await db.SaveChangesAsync(ct);

        return new AuthSession(accessToken, accessExpires, raw, refreshExpires, ToInfo(user));
    }

    private async Task RevokeAllAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, now), ct);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static UserInfo ToInfo(AppUser u) => new(u.Id, u.Email ?? string.Empty, u.DisplayName);

    private static void Add(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
        list.Add(message);
    }

    private static ValidationException ToValidation(Dictionary<string, List<string>> errors) =>
        new(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
}
