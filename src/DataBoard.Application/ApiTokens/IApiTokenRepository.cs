using DataBoard.Domain.Users;

namespace DataBoard.Application.ApiTokens;

public interface IApiTokenRepository
{
    Task AddAsync(ApiToken token, CancellationToken ct = default);
    void Remove(ApiToken token);

    /// <summary>A tracked entity, or null if the user has no such token.</summary>
    Task<ApiToken?> GetAsync(Guid userId, Guid tokenId, CancellationToken ct = default);

    Task<IReadOnlyList<ApiToken>> ListForUserAsync(Guid userId, CancellationToken ct = default);

    Task<int> CountForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Finds a token by the hash of its secret (tracked, so its last use can be noted).</summary>
    Task<ApiToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);
}
