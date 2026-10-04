using DataBoard.Application.Dashboards;
using DataBoard.Domain.Dashboards;
using Microsoft.EntityFrameworkCore;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DashboardOrgShareRepository(AppDbContext db) : IDashboardOrgShareRepository
{
    public async Task AddAsync(DashboardOrgShare share, CancellationToken ct = default) =>
        await db.DashboardOrgShares.AddAsync(share, ct);

    public void Remove(DashboardOrgShare share) => db.DashboardOrgShares.Remove(share);

    public Task<DashboardOrgShare?> GetAsync(Guid dashboardId, Guid targetOrganizationId, CancellationToken ct = default) =>
        db.DashboardOrgShares.SingleOrDefaultAsync(s => s.DashboardId == dashboardId && s.TargetOrganizationId == targetOrganizationId, ct);

    public async Task<IReadOnlyList<DashboardOrgShare>> ListForDashboardAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default) =>
        await db.DashboardOrgShares.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && s.DashboardId == dashboardId)
            .OrderBy(s => s.CreatedUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DashboardOrgShare>> ListForTargetAsync(Guid targetOrganizationId, CancellationToken ct = default) =>
        await db.DashboardOrgShares.AsNoTracking().Where(s => s.TargetOrganizationId == targetOrganizationId).ToListAsync(ct);

    public Task<int> CountForDashboardAsync(Guid dashboardId, CancellationToken ct = default) =>
        db.DashboardOrgShares.CountAsync(s => s.DashboardId == dashboardId, ct);
}
