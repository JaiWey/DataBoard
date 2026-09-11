using DataBoard.Application.Dashboards;
using DataBoard.Domain.Dashboards;
using Microsoft.EntityFrameworkCore;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DashboardShareRepository(AppDbContext db) : IDashboardShareRepository
{
    public async Task AddAsync(DashboardShare share, CancellationToken ct = default) =>
        await db.DashboardShares.AddAsync(share, ct);

    public void Remove(DashboardShare share) => db.DashboardShares.Remove(share);

    public Task<DashboardShare?> GetAsync(Guid organizationId, Guid dashboardId, Guid shareId, CancellationToken ct = default) =>
        db.DashboardShares.SingleOrDefaultAsync(
            s => s.OrganizationId == organizationId && s.DashboardId == dashboardId && s.Id == shareId, ct);

    public async Task<IReadOnlyList<DashboardShare>> ListAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default) =>
        await db.DashboardShares.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && s.DashboardId == dashboardId)
            .OrderByDescending(s => s.CreatedUtc)
            .ToListAsync(ct);

    public Task<int> CountActiveAsync(Guid dashboardId, DateTime nowUtc, CancellationToken ct = default) =>
        db.DashboardShares.CountAsync(s => s.DashboardId == dashboardId && (s.ExpiresUtc == null || s.ExpiresUtc > nowUtc), ct);

    public Task<DashboardShare?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.DashboardShares.AsNoTracking().SingleOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
}
