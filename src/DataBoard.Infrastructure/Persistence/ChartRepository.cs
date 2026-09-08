using DataBoard.Application.Charts;
using DataBoard.Domain.Charts;
using Microsoft.EntityFrameworkCore;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class ChartRepository(AppDbContext db) : IChartRepository
{
    public async Task AddAsync(Chart chart, CancellationToken ct = default) => await db.Charts.AddAsync(chart, ct);

    public void Remove(Chart chart) => db.Charts.Remove(chart);

    public Task<Chart?> GetAsync(Guid organizationId, Guid chartId, CancellationToken ct = default) =>
        db.Charts.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == chartId, ct);

    public async Task<IReadOnlyList<Chart>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
        await db.Charts.AsNoTracking().Where(c => c.OrganizationId == organizationId).OrderBy(c => c.Name).ToListAsync(ct);

    public Task<bool> NameExistsAsync(Guid organizationId, string name, Guid? excludeChartId, CancellationToken ct = default)
    {
        var lower = name.ToLowerInvariant();
        return db.Charts.AnyAsync(c => c.OrganizationId == organizationId && c.Name.ToLower() == lower && c.Id != excludeChartId, ct);
    }
}
