using DataBoard.Application.Abstractions;
using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Dashboards;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Dashboards;

/// <param name="OrganizationId">The organization to show the dashboard to. You have to belong to it.</param>
public sealed record ShareWithOrganizationRequest(Guid OrganizationId);

/// <param name="OrganizationName">Null when the person asking is not in that organization, so its name is not theirs to know.</param>
public sealed record OrgShareDto(Guid OrganizationId, string? OrganizationName, DateTime SharedUtc);

/// <summary>A dashboard another organization shares with this one, as a row in the list.</summary>
/// <param name="OrganizationId">The organization that owns it (the one to ask for it).</param>
public sealed record SharedWithMeDto(
    Guid DashboardId, Guid OrganizationId, string OrganizationName, string Name, string? Description, int WidgetCount, DateTime UpdatedUtc);

/// <summary>
/// Shows a dashboard to every member of another organization, live and read-only. Editors and Owners of the dashboard's
/// organization do it, and only with organizations they belong to themselves, so nobody can push a dashboard into (or
/// find out about) an organization they are not part of. Doing it again is not an error.
/// </summary>
public sealed class ShareWithOrganizationHandler(
    IDashboardOrgShareRepository shares, IDashboardRepository dashboards, IOrganizationRepository organizations,
    IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<OrgShareDto> HandleAsync(Guid organizationId, Guid dashboardId, ShareWithOrganizationRequest? request, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();
        _ = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);

        var target = request?.OrganizationId ?? Guid.Empty;
        if (target == Guid.Empty) throw new ValidationException("OrganizationId", "Choose an organization to share with.");
        if (target == organizationId) throw new ValidationException("OrganizationId", "The dashboard is already in its own organization.");
        if (await organizations.GetMemberAsync(target, userId, ct) is null)
            throw new ValidationException("OrganizationId", "A dashboard can only be shared with organizations you belong to.");

        var name = (await organizations.GetNamesAsync([target], ct)).GetValueOrDefault(target);
        if (await shares.GetAsync(dashboardId, target, ct) is { } existing)
            return new OrgShareDto(target, name, existing.CreatedUtc);

        if (await shares.CountForDashboardAsync(dashboardId, ct) >= DashboardOrgShare.MaxPerDashboard)
            throw new ValidationException("OrganizationId", $"A dashboard can be shared with at most {DashboardOrgShare.MaxPerDashboard} organizations.");

        var share = DatasetQuery.Guard(() => DashboardOrgShare.Create(organizationId, dashboardId, target, userId, clock.UtcNow));
        await shares.AddAsync(share, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return new OrgShareDto(target, name, share.CreatedUtc);
    }
}

public sealed class ListOrgSharesHandler(
    IDashboardOrgShareRepository shares, IDashboardRepository dashboards, IOrganizationRepository organizations, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<IReadOnlyList<OrgShareDto>> HandleAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();
        _ = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);

        var items = await shares.ListForDashboardAsync(organizationId, dashboardId, ct);
        var mine = (await organizations.ListForUserAsync(userId, ct)).ToDictionary(o => o.Id, o => o.Name);
        return items.Select(s => new OrgShareDto(s.TargetOrganizationId, mine.GetValueOrDefault(s.TargetOrganizationId), s.CreatedUtc)).ToList();
    }
}

/// <summary>Stops showing the dashboard to an organization. Any Editor of the dashboard's organization may, whoever made the share.</summary>
public sealed class UnshareWithOrganizationHandler(
    IDashboardOrgShareRepository shares, IDashboardRepository dashboards, IUnitOfWork unitOfWork, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task HandleAsync(Guid organizationId, Guid dashboardId, Guid targetOrganizationId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        _ = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);
        var share = await shares.GetAsync(dashboardId, targetOrganizationId, ct);
        if (share is null || share.OrganizationId != organizationId) throw new NotFoundException("That dashboard is not shared with that organization.");

        shares.Remove(share);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>What other organizations show to this one. Any member may look.</summary>
public sealed class ListSharedWithMeHandler(
    IDashboardOrgShareRepository shares, IDashboardRepository dashboards, IOrganizationRepository organizations, OrganizationAccess access)
{
    public async Task<IReadOnlyList<SharedWithMeDto>> HandleAsync(Guid organizationId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);

        var items = await shares.ListForTargetAsync(organizationId, ct);
        var names = await organizations.GetNamesAsync(items.Select(s => s.OrganizationId).Distinct().ToList(), ct);

        var result = new List<SharedWithMeDto>();
        foreach (var share in items)
        {
            var dashboard = await dashboards.GetAsync(share.OrganizationId, share.DashboardId, ct);
            if (dashboard is null) continue;
            var widgets = DashboardJson.Deserialize(dashboard.DefinitionJson).Widgets.Count(w => w.Kind == WidgetKind.Chart);
            result.Add(new SharedWithMeDto(
                dashboard.Id, share.OrganizationId, names.GetValueOrDefault(share.OrganizationId) ?? "Another organization",
                dashboard.Name, dashboard.Description, widgets, dashboard.UpdatedUtc));
        }

        return result
            .OrderBy(r => r.OrganizationName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

/// <summary>
/// Whether the current person may open a dashboard another organization shares with theirs: they belong to the receiving
/// organization (any role) and the dashboard is shared with it. Anything else is the same "not found", so nothing says
/// whether a dashboard of another organization exists.
/// </summary>
public sealed class OrgSharedView(
    IDashboardOrgShareRepository shares, IDashboardRepository dashboards, IOrganizationRepository organizations, OrganizationAccess access)
{
    public async Task<(Dashboard Dashboard, string OrganizationName)> ResolveAsync(
        Guid targetOrganizationId, Guid sourceOrganizationId, Guid dashboardId, CancellationToken ct)
    {
        await access.RequireRoleAsync(targetOrganizationId, OrganizationRole.Viewer, ct);

        var share = await shares.GetAsync(dashboardId, targetOrganizationId, ct);
        if (share is null || share.OrganizationId != sourceOrganizationId) throw NotShared();
        var dashboard = await dashboards.GetAsync(sourceOrganizationId, dashboardId, ct) ?? throw NotShared();

        var name = (await organizations.GetNamesAsync([sourceOrganizationId], ct)).GetValueOrDefault(sourceOrganizationId);
        return (dashboard, name ?? "Another organization");
    }

    private static NotFoundException NotShared() => new("This dashboard is not shared with your organization.");
}

/// <summary>The page of a shared dashboard: the same read-only view a public link shows.</summary>
public sealed class GetOrgSharedDashboardHandler(OrgSharedView view, IChartRepository charts)
{
    public async Task<SharedDashboardDto> HandleAsync(Guid targetOrganizationId, Guid sourceOrganizationId, Guid dashboardId, CancellationToken ct = default)
    {
        var (dashboard, owner) = await view.ResolveAsync(targetOrganizationId, sourceOrganizationId, dashboardId, ct);
        return await SharedPages.BuildAsync(charts, sourceOrganizationId, dashboard, expiresUtc: null, sharedBy: owner, ct);
    }
}

public sealed class RunOrgSharedDashboardHandler(OrgSharedView view, RunDashboardHandler runner)
{
    public async Task<DashboardRunResult> HandleAsync(
        Guid targetOrganizationId, Guid sourceOrganizationId, Guid dashboardId, RunDashboardRequest? request, CancellationToken ct = default)
    {
        var (dashboard, _) = await view.ResolveAsync(targetOrganizationId, sourceOrganizationId, dashboardId, ct);
        // Run as a visitor does (no SQL comes back, no forced refresh), against the data of the organization that owns it.
        return await runner.HandleSharedAsync(sourceOrganizationId, DashboardJson.Deserialize(dashboard.DefinitionJson), request, ct);
    }
}

public sealed class GetOrgSharedBackgroundHandler(OrgSharedView view, IImageStore images)
{
    public async Task<BackgroundImage> HandleAsync(Guid targetOrganizationId, Guid sourceOrganizationId, Guid dashboardId, CancellationToken ct = default)
    {
        var (dashboard, _) = await view.ResolveAsync(targetOrganizationId, sourceOrganizationId, dashboardId, ct);
        return GetDashboardBackgroundHandler.Open(dashboard, images);
    }
}

public sealed class GetOrgSharedImageWidgetHandler(OrgSharedView view, IImageStore images)
{
    public async Task<BackgroundImage> HandleAsync(
        Guid targetOrganizationId, Guid sourceOrganizationId, Guid dashboardId, Guid widgetId, CancellationToken ct = default)
    {
        var (dashboard, _) = await view.ResolveAsync(targetOrganizationId, sourceOrganizationId, dashboardId, ct);
        return GetImageWidgetHandler.Open(dashboard, widgetId, images);
    }
}
