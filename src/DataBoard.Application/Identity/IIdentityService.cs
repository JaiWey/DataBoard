namespace DataBoard.Application.Identity;

public sealed record UserInfo(Guid Id, string Email, string DisplayName);

public sealed record AuthSession(
    string AccessToken,
    DateTime AccessTokenExpiresUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresUtc,
    UserInfo User);

/// <summary>Who another service (GitHub) says someone is. Only what the sign-in needs.</summary>
/// <param name="Provider">The service, such as "GitHub".</param>
/// <param name="ProviderKey">The person's id at that service. It never changes, unlike a name or an email.</param>
/// <param name="Email">The email the service vouches for, if any. Without a verified one nobody is signed in.</param>
public sealed record ExternalIdentity(string Provider, string ProviderKey, string? Email, bool EmailVerified, string? DisplayName);

public interface IIdentityService
{
    /// <summary>
    /// Signs in someone who proved who they are to another service. Someone seen before is signed in; a verified email that
    /// belongs to an account links the service to it; otherwise a new account is made.
    /// </summary>
    /// <exception cref="Common.UnauthorizedException">No verified email, the account is locked, or the account could not be made.</exception>
    Task<AuthSession> SignInExternalAsync(ExternalIdentity identity, CancellationToken ct = default);

    /// <exception cref="Common.ValidationException">Email or password rejected, or email already registered.</exception>
    Task<UserInfo> RegisterAsync(string email, string password, string displayName, CancellationToken ct = default);

    /// <exception cref="Common.UnauthorizedException">Unknown user, wrong password or locked out (same message for all).</exception>
    /// <summary>
    /// A temporary account with a sandbox organization of its own (it is never put in the default organization, which
    /// holds other people's work). The session ends with the account.
    /// </summary>
    /// <exception cref="Common.ForbiddenException">Guests are turned off or too many are here already.</exception>
    Task<AuthSession> StartGuestAsync(CancellationToken ct = default);

    /// <summary>Deletes guests whose time is up, with their sandbox organizations. Returns how many.</summary>
    Task<int> DeleteExpiredGuestsAsync(CancellationToken ct = default);

    /// <exception cref="Common.UnauthorizedException">Unknown user, wrong password or locked out (same message for all).</exception>
    Task<AuthSession> LoginAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Rotates the refresh token: the presented one stops working.</summary>
    /// <exception cref="Common.UnauthorizedException">Token unknown, expired or already used.</exception>
    Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    Task<UserInfo?> GetUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Who these ids are. An id with no account (deleted) is left out.</summary>
    Task<IReadOnlyList<UserInfo>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken ct = default);
}
