using DataBoard.Application.Datasets;
using DataBoard.Domain.Datasets;
using Microsoft.EntityFrameworkCore;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DatasetRepository(AppDbContext db) : IDatasetRepository
{
    public async Task AddAsync(Dataset dataset, CancellationToken ct = default) =>
        await db.Datasets.AddAsync(dataset, ct);

    public void Remove(Dataset dataset) => db.Datasets.Remove(dataset);

    public Task<Dataset?> GetAsync(Guid organizationId, Guid datasetId, CancellationToken ct = default) =>
        db.Datasets.SingleOrDefaultAsync(d => d.OrganizationId == organizationId && d.Id == datasetId, ct);

    public async Task<IReadOnlyList<Dataset>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
        await db.Datasets
            .AsNoTracking()
            .Where(d => d.OrganizationId == organizationId)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

    public Task<bool> NameExistsAsync(Guid organizationId, string name, Guid? excludeDatasetId, CancellationToken ct = default)
    {
        // Compared case-insensitively so "Sales" and "sales" cannot both exist.
        var lower = name.ToLowerInvariant();
        return db.Datasets.AnyAsync(
            d => d.OrganizationId == organizationId && d.Name.ToLower() == lower && d.Id != excludeDatasetId, ct);
    }
}
