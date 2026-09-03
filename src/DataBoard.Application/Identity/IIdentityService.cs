namespace DataBoard.Application.Identity;

public sealed record UserInfo(Guid Id, string Email, string DisplayName);

public sealed record AuthSession(
    string AccessToken,
    DateTime AccessTokenExpiresUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresUtc,
    UserInfo User);

public interface IIdentityService
{
    /// <exception cref="Common.ValidationException">Email or password rejected, or email already registered.</exception>
    Task<UserInfo> RegisterAsync(string email, string password, string displayName, CancellationToken ct = default);

    /// <exception cref="Common.UnauthorizedException">Unknown user, wrong password or locked out (same message for all).</exception>
    Task<AuthSession> LoginAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Rotates the refresh token: the presented one stops working.</summary>
    /// <exception cref="Common.UnauthorizedException">Token unknown, expired or already used.</exception>
    Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    Task<UserInfo?> GetUserAsync(Guid userId, CancellationToken ct = default);

    Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken ct = default);
}
