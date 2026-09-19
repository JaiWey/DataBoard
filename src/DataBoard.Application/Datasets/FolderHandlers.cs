using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Datasets;

public interface IDatasetFolderRepository
{
    Task AddAsync(DatasetFolder folder, CancellationToken ct = default);
    void Remove(DatasetFolder folder);

    /// <summary>Returns a tracked entity (so it can be updated or removed), or null if it is not in that organization.</summary>
    Task<DatasetFolder?> GetAsync(Guid organizationId, Guid folderId, CancellationToken ct = default);

    Task<IReadOnlyList<DatasetFolder>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>Is there already a folder with this name directly under the parent (compared case-insensitively)?</summary>
    Task<bool> NameExistsAsync(Guid organizationId, Guid? parentId, string name, Guid? excludeFolderId, CancellationToken ct = default);

    /// <summary>Does the folder hold any dataset or subfolder?</summary>
    Task<bool> HasContentsAsync(Guid folderId, CancellationToken ct = default);
}

public sealed record CreateFolderRequest(string Name, Guid? ParentId);

/// <summary>Renames a folder and/or moves it; the parent is always given (null is the top level).</summary>
public sealed record UpdateFolderRequest(string Name, Guid? ParentId);

public sealed record MoveDatasetRequest(Guid? FolderId);

public sealed record DatasetFolderDto(Guid Id, Guid? ParentId, string Name);

internal static class FolderRules
{
    public static DatasetFolderDto ToDto(this DatasetFolder f) => new(f.Id, f.ParentId, f.Name);

    /// <summary>The parent must exist in the organization (null, the top level, always does).</summary>
    public static async Task RequireFolderAsync(
        IDatasetFolderRepository folders, Guid organizationId, Guid? folderId, string field, CancellationToken ct)
    {
        if (folderId is { } id && await folders.GetAsync(organizationId, id, ct) is null)
            throw new ValidationException(field, "That folder does not exist.");
    }

    /// <summary>How many levels deep a folder sits: a top level folder is 1.</summary>
    public static int DepthOf(IReadOnlyDictionary<Guid, DatasetFolder> byId, Guid? folderId)
    {
        var depth = 0;
        var seen = new HashSet<Guid>();
        while (folderId is { } id && byId.TryGetValue(id, out var folder) && seen.Add(id))
        {
            depth++;
            folderId = folder.ParentId;
        }
        return depth;
    }

    /// <summary>How many levels the folder and everything below it take (a folder with no subfolders is 1).</summary>
    public static int HeightOf(ILookup<Guid?, DatasetFolder> children, Guid folderId)
    {
        var tallest = 0;
        foreach (var child in children[folderId])
            tallest = Math.Max(tallest, HeightOf(children, child.Id));
        return tallest + 1;
    }
}

public sealed class ListFoldersHandler(IDatasetFolderRepository folders, OrganizationAccess access)
{
    public async Task<IReadOnlyList<DatasetFolderDto>> HandleAsync(Guid organizationId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var items = await folders.ListByOrganizationAsync(organizationId, ct);
        return items.Select(f => f.ToDto()).ToList();
    }
}

public sealed class CreateFolderHandler(
    IDatasetFolderRepository folders, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<DatasetFolderDto> HandleAsync(Guid organizationId, CreateFolderRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();

        var all = (await folders.ListByOrganizationAsync(organizationId, ct)).ToDictionary(f => f.Id);
        if (request.ParentId is { } parentId)
        {
            if (!all.ContainsKey(parentId))
                throw new ValidationException("ParentId", "That folder does not exist.");
            if (FolderRules.DepthOf(all, parentId) >= DatasetFolder.MaxDepth)
                throw new ValidationException("ParentId", $"Folders can be nested at most {DatasetFolder.MaxDepth} levels deep.");
        }

        var folder = DatasetQuery.Guard(() => DatasetFolder.Create(organizationId, request.ParentId, request.Name, userId, clock.UtcNow));
        if (await folders.NameExistsAsync(organizationId, folder.ParentId, folder.Name, null, ct))
            throw new ConflictException("A folder with that name already exists here.");

        await folders.AddAsync(folder, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return folder.ToDto();
    }
}

/// <summary>Renames a folder, moves it, or both.</summary>
public sealed class UpdateFolderHandler(
    IDatasetFolderRepository folders, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<DatasetFolderDto> HandleAsync(Guid organizationId, Guid folderId, UpdateFolderRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var folder = await folders.GetAsync(organizationId, folderId, ct)
            ?? throw new NotFoundException("Folder not found.");

        if (request.ParentId != folder.ParentId)
        {
            var all = await folders.ListByOrganizationAsync(organizationId, ct);
            var byId = all.ToDictionary(f => f.Id);
            var children = all.ToLookup(f => f.ParentId);

            if (request.ParentId is { } parentId)
            {
                if (!byId.ContainsKey(parentId))
                    throw new ValidationException("ParentId", "That folder does not exist.");

                // Walking up from the new parent must never reach the folder: that would put it inside itself.
                for (Guid? up = parentId; up is { } id && byId.TryGetValue(id, out var ancestor); up = ancestor.ParentId)
                {
                    if (id == folderId)
                        throw new ValidationException("ParentId", "A folder cannot be moved into itself or one of its own subfolders.");
                }
            }

            if (FolderRules.DepthOf(byId, request.ParentId) + FolderRules.HeightOf(children, folderId) > DatasetFolder.MaxDepth)
                throw new ValidationException("ParentId", $"Folders can be nested at most {DatasetFolder.MaxDepth} levels deep.");
        }

        DatasetQuery.Guard(() =>
        {
            folder.Rename(request.Name, clock.UtcNow);
            folder.MoveTo(request.ParentId, clock.UtcNow);
            return true;
        });

        if (await folders.NameExistsAsync(organizationId, folder.ParentId, folder.Name, folder.Id, ct))
            throw new ConflictException("A folder with that name already exists here.");

        await unitOfWork.SaveChangesAsync(ct);
        return folder.ToDto();
    }
}

/// <summary>Only an empty folder can be deleted, so nothing is ever lost by accident.</summary>
public sealed class DeleteFolderHandler(IDatasetFolderRepository folders, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task HandleAsync(Guid organizationId, Guid folderId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var folder = await folders.GetAsync(organizationId, folderId, ct)
            ?? throw new NotFoundException("Folder not found.");
        if (await folders.HasContentsAsync(folderId, ct))
            throw new ConflictException("This folder is not empty. Move or delete what is inside first.");

        folders.Remove(folder);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

public sealed class MoveDatasetHandler(
    IDatasetRepository datasets, IDatasetFolderRepository folders, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task<DatasetDto> HandleAsync(Guid organizationId, Guid datasetId, MoveDatasetRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var dataset = await datasets.GetAsync(organizationId, datasetId, ct)
            ?? throw new NotFoundException("Dataset not found.");
        await FolderRules.RequireFolderAsync(folders, organizationId, request.FolderId, "FolderId", ct);

        dataset.MoveTo(request.FolderId);
        await unitOfWork.SaveChangesAsync(ct);
        return dataset.ToDto(includeSql: true);
    }
}
