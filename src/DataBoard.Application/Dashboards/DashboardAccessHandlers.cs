using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.Folders;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Dashboards;
using DataBoard.Domain.Folders;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Dashboards;

/// <summary>Who may see a dashboard or a folder. Sending nothing (no role, no people) opens it to everyone in the organization.</summary>
public sealed record SetAccessRequest(OrganizationRole? MinRole, IReadOnlyList<Guid>? MemberIds);

/// <summary>A folder above this one that has a rule of its own, which applies as well as this one's.</summary>
public sealed record InheritedAccessDto(Guid FolderId, string FolderName, OrganizationRole? MinRole, int People);

/// <param name="MinRole">People with at least this role may see it. Null: no role is let in as such.</param>
/// <param name="MemberIds">People who may see it by name.</param>
/// <param name="Inherited">Folders above it that are limited too (everything inside a limited folder is).</param>
public sealed record AccessDto(OrganizationRole? MinRole, IReadOnlyList<Guid> MemberIds, IReadOnlyList<InheritedAccessDto> Inherited);

/// <summary>
/// Who may see which dashboards and dashboard folders. A rule on a folder applies to everything inside it, subfolders
/// included, and a thing is seen only by somebody every rule on the way lets in (the strictest wins). Owners of the
/// organization see everything. What is hidden is simply not found: not listed, not opened, not run, not changed.
/// </summary>
public sealed class DashboardVisibility(IFolderRepository folders)
{
    private Guid _loadedFor;
    private Dictionary<Guid, Folder>? _folders;

    private async Task<Dictionary<Guid, Folder>> FoldersAsync(Guid organizationId, CancellationToken ct)
    {
        if (_folders is null || _loadedFor != organizationId)
        {
            _folders = (await folders.ListByOrganizationAsync(organizationId, FolderKind.Dashboard, ct)).ToDictionary(f => f.Id);
            _loadedFor = organizationId;
        }
        return _folders;
    }

    /// <summary>The rules from the thing itself up through every folder around it.</summary>
    private async Task<bool> AllowedAsync(OrganizationMember member, Guid organizationId, AccessRule own, Guid? folderId, CancellationToken ct)
    {
        if (!own.Allows(member.Role, member.UserId)) return false;
        if (folderId is null) return true;

        var all = await FoldersAsync(organizationId, ct);
        var seen = new HashSet<Guid>();
        for (var up = folderId; up is { } id && all.TryGetValue(id, out var folder) && seen.Add(id); up = folder.ParentId)
            if (!folder.Access.Allows(member.Role, member.UserId)) return false;
        return true;
    }

    public Task<bool> CanSeeAsync(OrganizationMember member, Dashboard dashboard, CancellationToken ct) =>
        AllowedAsync(member, dashboard.OrganizationId, dashboard.Access, dashboard.FolderId, ct);

    public Task<bool> CanSeeAsync(OrganizationMember member, Folder folder, CancellationToken ct) =>
        AllowedAsync(member, folder.OrganizationId, folder.Access, folder.ParentId, ct);

    /// <summary>The dashboard, if it is there and the person may see it; otherwise the same "not found" for both.</summary>
    public async Task<Dashboard> GetAsync(
        IDashboardRepository dashboards, Guid organizationId, Guid dashboardId, OrganizationMember member, CancellationToken ct)
    {
        var dashboard = await dashboards.GetAsync(organizationId, dashboardId, ct) ?? throw new NotFoundException("Dashboard not found.");
        if (!await CanSeeAsync(member, dashboard, ct)) throw new NotFoundException("Dashboard not found.");
        return dashboard;
    }

    /// <summary>The folder, if it is a dashboard folder, it is there and the person may see it.</summary>
    public async Task<Folder> GetFolderAsync(Guid organizationId, Guid folderId, OrganizationMember member, CancellationToken ct)
    {
        var all = await FoldersAsync(organizationId, ct);
        if (!all.TryGetValue(folderId, out var folder) || !await CanSeeAsync(member, folder, ct))
            throw new NotFoundException("Folder not found.");
        return folder;
    }

    /// <summary>A place to file a dashboard: the top level, or a dashboard folder the person may see.</summary>
    public async Task RequireFolderAsync(OrganizationMember member, Guid organizationId, Guid? folderId, string field, CancellationToken ct)
    {
        if (folderId is not { } id) return;
        var all = await FoldersAsync(organizationId, ct);
        if (!all.TryGetValue(id, out var folder) || !await CanSeeAsync(member, folder, ct))
            throw new ValidationException(field, "That folder does not exist.");
    }

    public async Task<IReadOnlyList<Dashboard>> FilterAsync(
        OrganizationMember member, IEnumerable<Dashboard> items, CancellationToken ct)
    {
        var result = new List<Dashboard>();
        foreach (var item in items)
            if (await CanSeeAsync(member, item, ct)) result.Add(item);
        return result;
    }

    public async Task<IReadOnlyList<Folder>> FilterFoldersAsync(OrganizationMember member, IEnumerable<Folder> items, CancellationToken ct)
    {
        var result = new List<Folder>();
        foreach (var item in items)
            if (await CanSeeAsync(member, item, ct)) result.Add(item);
        return result;
    }

    /// <summary>The limited folders above this place, nearest first.</summary>
    public async Task<IReadOnlyList<InheritedAccessDto>> InheritedAsync(Guid organizationId, Guid? folderId, CancellationToken ct)
    {
        var inherited = new List<InheritedAccessDto>();
        if (folderId is null) return inherited;

        var all = await FoldersAsync(organizationId, ct);
        var seen = new HashSet<Guid>();
        for (var up = folderId; up is { } id && all.TryGetValue(id, out var folder) && seen.Add(id); up = folder.ParentId)
            if (!folder.Access.IsOpen) inherited.Add(new InheritedAccessDto(folder.Id, folder.Name, folder.AccessMinRole, folder.AccessMemberIds.Length));
        return inherited;
    }
}

/// <summary>Checks a rule somebody wants to set: real people of this organization, and one that does not lock the person out.</summary>
internal static class AccessRules
{
    public static async Task<AccessRule> CheckAsync(
        SetAccessRequest request, OrganizationMember setter, IOrganizationRepository organizations, CancellationToken ct)
    {
        var rule = DatasetQuery.Guard(() => AccessRule.Create(request.MinRole, request.MemberIds));

        foreach (var userId in rule.MemberIds)
            if (await organizations.GetMemberAsync(setter.OrganizationId, userId, ct) is null)
                throw new ValidationException("MemberIds", "Only people who are members of this organization can be listed.");

        if (!rule.Allows(setter.Role, setter.UserId))
            throw new ValidationException("MinRole", "That would lock you out of it. Choose a role you have, or add yourself to the list.");
        return rule;
    }

    public static AccessDto ToDto(AccessRule rule, IReadOnlyList<InheritedAccessDto> inherited) => new(rule.MinRole, rule.MemberIds, inherited);
}

/// <summary>Who may see a dashboard. Editors and Owners who can see it themselves may look and change it.</summary>
public sealed class GetDashboardAccessHandler(IDashboardRepository dashboards, DashboardVisibility visibility, OrganizationAccess access)
{
    public async Task<AccessDto> HandleAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var dashboard = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);
        return AccessRules.ToDto(dashboard.Access, await visibility.InheritedAsync(organizationId, dashboard.FolderId, ct));
    }
}

public sealed class SetDashboardAccessHandler(
    IDashboardRepository dashboards, IOrganizationRepository organizations, DashboardVisibility visibility, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task<AccessDto> HandleAsync(Guid organizationId, Guid dashboardId, SetAccessRequest? request, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var dashboard = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);

        var rule = await AccessRules.CheckAsync(request ?? new SetAccessRequest(null, null), member, organizations, ct);
        dashboard.SetAccess(rule);
        await unitOfWork.SaveChangesAsync(ct);
        return AccessRules.ToDto(dashboard.Access, await visibility.InheritedAsync(organizationId, dashboard.FolderId, ct));
    }
}

/// <summary>Who may see a dashboard folder, and so everything in it. Only dashboard folders can be limited.</summary>
public sealed class GetFolderAccessHandler(DashboardVisibility visibility, OrganizationAccess access)
{
    public async Task<AccessDto> HandleAsync(Guid organizationId, Guid folderId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var folder = await visibility.GetFolderAsync(organizationId, folderId, member, ct);
        return AccessRules.ToDto(folder.Access, await visibility.InheritedAsync(organizationId, folder.ParentId, ct));
    }
}

public sealed class SetFolderAccessHandler(
    IOrganizationRepository organizations, DashboardVisibility visibility, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task<AccessDto> HandleAsync(Guid organizationId, Guid folderId, SetAccessRequest? request, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var folder = await visibility.GetFolderAsync(organizationId, folderId, member, ct);

        var rule = await AccessRules.CheckAsync(request ?? new SetAccessRequest(null, null), member, organizations, ct);
        folder.SetAccess(rule);
        await unitOfWork.SaveChangesAsync(ct);
        return AccessRules.ToDto(folder.Access, await visibility.InheritedAsync(organizationId, folder.ParentId, ct));
    }
}
