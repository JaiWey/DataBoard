using DataBoard.Application.Dashboards;
using DataBoard.Domain.Dashboards;
using Microsoft.EntityFrameworkCore;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DashboardRepository(AppDbContext db) : IDashboardRepository
{
    public async Task AddAsync(Dashboard dashboard, CancellationToken ct = default) =>
        await db.Dashboards.AddAsync(dashboard, ct);

    public void Remove(Dashboard dashboard) => db.Dashboards.Remove(dashboard);

    public Task<Dashboard?> GetAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default) =>
        db.Dashboards.SingleOrDefaultAsync(d => d.OrganizationId == organizationId && d.Id == dashboardId, ct);

    public async Task<IReadOnlyList<Dashboard>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
        await db.Dashboards.AsNoTracking().Where(d => d.OrganizationId == organizationId).OrderBy(d => d.Name).ToListAsync(ct);

    public Task<bool> NameExistsAsync(Guid organizationId, string name, Guid? excludeDashboardId, CancellationToken ct = default)
    {
        var lower = name.ToLowerInvariant();
        return db.Dashboards.AnyAsync(
            d => d.OrganizationId == organizationId && d.Name.ToLower() == lower && d.Id != excludeDashboardId, ct);
    }
}
