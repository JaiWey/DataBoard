using DataBoard.Application.Datasets;
using DataBoard.Domain.Datasets;
using Microsoft.EntityFrameworkCore;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DatasetFolderRepository(AppDbContext db) : IDatasetFolderRepository
{
    public async Task AddAsync(DatasetFolder folder, CancellationToken ct = default) =>
        await db.DatasetFolders.AddAsync(folder, ct);

    public void Remove(DatasetFolder folder) => db.DatasetFolders.Remove(folder);

    public Task<DatasetFolder?> GetAsync(Guid organizationId, Guid folderId, CancellationToken ct = default) =>
        db.DatasetFolders.SingleOrDefaultAsync(f => f.OrganizationId == organizationId && f.Id == folderId, ct);

    /// <summary>Tracked, so a move or rename of one of them is saved with the rest.</summary>
    public async Task<IReadOnlyList<DatasetFolder>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
        await db.DatasetFolders.Where(f => f.OrganizationId == organizationId).OrderBy(f => f.Name).ToListAsync(ct);

    public Task<bool> NameExistsAsync(Guid organizationId, Guid? parentId, string name, Guid? excludeFolderId, CancellationToken ct = default)
    {
        var lower = name.ToLowerInvariant();
        return db.DatasetFolders.AnyAsync(
            f => f.OrganizationId == organizationId && f.ParentId == parentId && f.Name.ToLower() == lower && f.Id != excludeFolderId, ct);
    }

    public async Task<bool> HasContentsAsync(Guid folderId, CancellationToken ct = default) =>
        await db.DatasetFolders.AnyAsync(f => f.ParentId == folderId, ct)
        || await db.Datasets.AnyAsync(d => d.FolderId == folderId, ct);
}
