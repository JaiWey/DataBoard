using DataBoard.Application.Folders;
using DataBoard.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class FolderRepository(AppDbContext db) : IFolderRepository
{
    public async Task AddAsync(Folder folder, CancellationToken ct = default) =>
        await db.Folders.AddAsync(folder, ct);

    public void Remove(Folder folder) => db.Folders.Remove(folder);

    public Task<Folder?> GetAsync(Guid organizationId, Guid folderId, CancellationToken ct = default) =>
        db.Folders.SingleOrDefaultAsync(f => f.OrganizationId == organizationId && f.Id == folderId, ct);

    /// <summary>Tracked, so a move or rename of one of them is saved with the rest.</summary>
    public async Task<IReadOnlyList<Folder>> ListByOrganizationAsync(Guid organizationId, FolderKind kind, CancellationToken ct = default) =>
        await db.Folders.Where(f => f.OrganizationId == organizationId && f.Kind == kind).OrderBy(f => f.Name).ToListAsync(ct);

    public Task<bool> NameExistsAsync(
        Guid organizationId, FolderKind kind, Guid? parentId, string name, Guid? excludeFolderId, CancellationToken ct = default)
    {
        var lower = name.ToLowerInvariant();
        return db.Folders.AnyAsync(
            f => f.OrganizationId == organizationId && f.Kind == kind && f.ParentId == parentId && f.Name.ToLower() == lower && f.Id != excludeFolderId, ct);
    }

    public async Task<bool> HasContentsAsync(FolderKind kind, Guid folderId, CancellationToken ct = default) =>
        await db.Folders.AnyAsync(f => f.ParentId == folderId, ct)
        || kind switch
        {
            FolderKind.Dataset => await db.Datasets.AnyAsync(d => d.FolderId == folderId, ct),
            FolderKind.Chart => await db.Charts.AnyAsync(c => c.FolderId == folderId, ct),
            FolderKind.Dashboard => await db.Dashboards.AnyAsync(d => d.FolderId == folderId, ct),
            _ => false,
        };
}
