using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Organizations;
using DataBoard.Application.Charts;
using DataBoard.Application.Dashboards;
using DataBoard.Application.Datasets;
using DataBoard.Domain.Folders;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Folders;

public interface IFolderRepository
{
    Task AddAsync(Folder folder, CancellationToken ct = default);
    void Remove(Folder folder);

    /// <summary>Returns a tracked entity (so it can be updated or removed), or null if it is not in that organization.</summary>
    Task<Folder?> GetAsync(Guid organizationId, Guid folderId, CancellationToken ct = default);

    /// <summary>The folders of one kind (datasets, charts or dashboards), tracked.</summary>
    Task<IReadOnlyList<Folder>> ListByOrganizationAsync(Guid organizationId, FolderKind kind, CancellationToken ct = default);

    /// <summary>Is there already a folder of this kind with this name directly under the parent (compared case-insensitively)?</summary>
    Task<bool> NameExistsAsync(Guid organizationId, FolderKind kind, Guid? parentId, string name, Guid? excludeFolderId, CancellationToken ct = default);

    /// <summary>Does the folder hold any subfolder, or any dataset, chart or dashboard (whichever its kind is)?</summary>
    Task<bool> HasContentsAsync(FolderKind kind, Guid folderId, CancellationToken ct = default);
}

/// <param name="Kind">What the folder organizes. Defaults to datasets.</param>
public sealed record CreateFolderRequest(string Name, Guid? ParentId, FolderKind Kind = FolderKind.Dataset);

/// <summary>Renames a folder and/or moves it (within its own kind); the parent is always given (null is the top level).</summary>
public sealed record UpdateFolderRequest(string Name, Guid? ParentId);

/// <summary>Where to file a dataset, chart or dashboard; null is the top level.</summary>
public sealed record MoveToFolderRequest(Guid? FolderId);

public sealed record FolderDto(Guid Id, Guid? ParentId, string Name, FolderKind Kind = FolderKind.Dataset);


internal static class FolderRules
{
    public static FolderDto ToDto(this Folder f) => new(f.Id, f.ParentId, f.Name, f.Kind);

    /// <summary>The folder must exist in the organization and be of the right kind (null, the top level, always fits).</summary>
    public static async Task RequireFolderAsync(
        IFolderRepository folders, Guid organizationId, FolderKind kind, Guid? folderId, string field, CancellationToken ct)
    {
        if (folderId is { } id && (await folders.GetAsync(organizationId, id, ct))?.Kind != kind)
            throw new ValidationException(field, "That folder does not exist.");
    }

    /// <summary>How many levels deep a folder sits: a top level folder is 1.</summary>
    public static int DepthOf(IReadOnlyDictionary<Guid, Folder> byId, Guid? folderId)
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
    public static int HeightOf(ILookup<Guid?, Folder> children, Guid folderId)
    {
        var tallest = 0;
        foreach (var child in children[folderId])
            tallest = Math.Max(tallest, HeightOf(children, child.Id));
        return tallest + 1;
    }
}

public sealed class ListFoldersHandler(IFolderRepository folders, OrganizationAccess access)
{
    public async Task<IReadOnlyList<FolderDto>> HandleAsync(Guid organizationId, FolderKind kind = FolderKind.Dataset, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var items = await folders.ListByOrganizationAsync(organizationId, kind, ct);
        return items.Select(f => f.ToDto()).ToList();
    }
}

public sealed class CreateFolderHandler(
    IFolderRepository folders, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<FolderDto> HandleAsync(Guid organizationId, CreateFolderRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();
        if (!Enum.IsDefined(request.Kind))
            throw new ValidationException("Kind", "Unknown folder kind.");

        var all = (await folders.ListByOrganizationAsync(organizationId, request.Kind, ct)).ToDictionary(f => f.Id);
        if (request.ParentId is { } parentId)
        {
            if (!all.ContainsKey(parentId))
                throw new ValidationException("ParentId", "That folder does not exist.");
            if (FolderRules.DepthOf(all, parentId) >= Folder.MaxDepth)
                throw new ValidationException("ParentId", $"Folders can be nested at most {Folder.MaxDepth} levels deep.");
        }

        var folder = DatasetQuery.Guard(() => Folder.Create(organizationId, request.Kind, request.ParentId, request.Name, userId, clock.UtcNow));
        if (await folders.NameExistsAsync(organizationId, request.Kind, folder.ParentId, folder.Name, null, ct))
            throw new ConflictException("A folder with that name already exists here.");

        await folders.AddAsync(folder, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return folder.ToDto();
    }
}

/// <summary>Renames a folder, moves it, or both. A folder stays among folders of its own kind.</summary>
public sealed class UpdateFolderHandler(
    IFolderRepository folders, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<FolderDto> HandleAsync(Guid organizationId, Guid folderId, UpdateFolderRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var folder = await folders.GetAsync(organizationId, folderId, ct)
            ?? throw new NotFoundException("Folder not found.");

        if (request.ParentId != folder.ParentId)
        {
            var all = await folders.ListByOrganizationAsync(organizationId, folder.Kind, ct);
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

            if (FolderRules.DepthOf(byId, request.ParentId) + FolderRules.HeightOf(children, folderId) > Folder.MaxDepth)
                throw new ValidationException("ParentId", $"Folders can be nested at most {Folder.MaxDepth} levels deep.");
        }

        DatasetQuery.Guard(() =>
        {
            folder.Rename(request.Name, clock.UtcNow);
            folder.MoveTo(request.ParentId, clock.UtcNow);
            return true;
        });

        if (await folders.NameExistsAsync(organizationId, folder.Kind, folder.ParentId, folder.Name, folder.Id, ct))
            throw new ConflictException("A folder with that name already exists here.");

        await unitOfWork.SaveChangesAsync(ct);
        return folder.ToDto();
    }
}

/// <summary>Only an empty folder can be deleted, so nothing is ever lost by accident.</summary>
public sealed class DeleteFolderHandler(IFolderRepository folders, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task HandleAsync(Guid organizationId, Guid folderId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var folder = await folders.GetAsync(organizationId, folderId, ct)
            ?? throw new NotFoundException("Folder not found.");
        if (await folders.HasContentsAsync(folder.Kind, folderId, ct))
            throw new ConflictException("This folder is not empty. Move or delete what is inside first.");

        folders.Remove(folder);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

public sealed class MoveDatasetHandler(
    IDatasetRepository datasets, IFolderRepository folders, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task<DatasetDto> HandleAsync(Guid organizationId, Guid datasetId, MoveToFolderRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var dataset = await datasets.GetAsync(organizationId, datasetId, ct)
            ?? throw new NotFoundException("Dataset not found.");
        await FolderRules.RequireFolderAsync(folders, organizationId, FolderKind.Dataset, request.FolderId, "FolderId", ct);

        dataset.MoveTo(request.FolderId);
        await unitOfWork.SaveChangesAsync(ct);
        return dataset.ToDto(includeSql: true);
    }
}

public sealed class MoveChartHandler(
    IChartRepository charts, IFolderRepository folders, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task<ChartDto> HandleAsync(Guid organizationId, Guid chartId, MoveToFolderRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var chart = await charts.GetAsync(organizationId, chartId, ct)
            ?? throw new NotFoundException("Chart not found.");
        await FolderRules.RequireFolderAsync(folders, organizationId, FolderKind.Chart, request.FolderId, "FolderId", ct);

        chart.MoveTo(request.FolderId);
        await unitOfWork.SaveChangesAsync(ct);
        return chart.ToDto();
    }
}

public sealed class MoveDashboardHandler(
    IDashboardRepository dashboards, IFolderRepository folders, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, Guid dashboardId, MoveToFolderRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var dashboard = await dashboards.GetAsync(organizationId, dashboardId, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        await FolderRules.RequireFolderAsync(folders, organizationId, FolderKind.Dashboard, request.FolderId, "FolderId", ct);

        dashboard.MoveTo(request.FolderId);
        await unitOfWork.SaveChangesAsync(ct);
        return dashboard.ToDto();
    }
}