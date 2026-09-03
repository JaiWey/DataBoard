using DataBoard.Application.DataSources;
using DataBoard.Domain.DataSources;
using Microsoft.EntityFrameworkCore;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DataSourceRepository(AppDbContext db) : IDataSourceRepository
{
    public async Task AddAsync(DataSource dataSource, CancellationToken ct = default) =>
        await db.DataSources.AddAsync(dataSource, ct);

    public Task<DataSource?> GetAsync(Guid organizationId, Guid dataSourceId, CancellationToken ct = default) =>
        db.DataSources
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.OrganizationId == organizationId && d.Id == dataSourceId, ct);

    public async Task<IReadOnlyList<DataSource>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
        await db.DataSources
            .AsNoTracking()
            .Where(d => d.OrganizationId == organizationId)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);
}
