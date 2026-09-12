using DataBoard.Application.ApiTokens;
using DataBoard.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class ApiTokenRepository(AppDbContext db) : IApiTokenRepository
{
    public async Task AddAsync(ApiToken token, CancellationToken ct = default) => await db.ApiTokens.AddAsync(token, ct);

    public void Remove(ApiToken token) => db.ApiTokens.Remove(token);

    public Task<ApiToken?> GetAsync(Guid userId, Guid tokenId, CancellationToken ct = default) =>
        db.ApiTokens.SingleOrDefaultAsync(t => t.UserId == userId && t.Id == tokenId, ct);

    public async Task<IReadOnlyList<ApiToken>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        await db.ApiTokens.AsNoTracking().Where(t => t.UserId == userId).OrderByDescending(t => t.CreatedUtc).ToListAsync(ct);

    public Task<int> CountForUserAsync(Guid userId, CancellationToken ct = default) =>
        db.ApiTokens.CountAsync(t => t.UserId == userId, ct);

    public Task<ApiToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.ApiTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
}
