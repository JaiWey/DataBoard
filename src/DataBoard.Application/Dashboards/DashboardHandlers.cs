using System.Text.Json;
using System.Text.Json.Serialization;
using DataBoard.Application.Abstractions;
using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.Folders;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Dashboards;
using DataBoard.Domain.Folders;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Dashboards;

internal static class DashboardJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(DashboardDefinition definition) => JsonSerializer.Serialize(definition, Options);

    public static DashboardDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<DashboardDefinition>(json, Options)
        ?? new DashboardDefinition([], []);
}

internal static class DashboardMapping
{
    public static DashboardDto ToDto(this Dashboard d) =>
        new(d.Id, d.Name, d.Description, DashboardJson.Deserialize(d.DefinitionJson), d.CreatedUtc, d.UpdatedUtc,
            new DashboardRefreshDto(d.RefreshIntervalMinutes, d.NextRefreshUtc, d.LastRefreshUtc, d.LastRefreshFailures, d.LastRefreshMessage),
            d.FolderId, d.ToBackgroundDto(), !d.Access.IsOpen);
}

/// <summary>
/// Checks a dashboard definition before it is saved: sizes, ids, filter operators, and that every widget refers to a
/// chart of the organization (a chart was already checked when it was saved).
/// </summary>
internal static class DashboardValidator
{
    public const int MaxWidgets = 24;
    public const int MaxFilters = 10;
    public const int MaxTitleLength = 200;
    public const int GridColumns = 12;
    public const int MaxWidgetHeight = 20;
    public const int MaxRow = 1_000;

    private static readonly FilterOperator[] SharedFilterOperators =
        [FilterOperator.Eq, FilterOperator.NotEq, FilterOperator.In, FilterOperator.Contains, FilterOperator.StartsWith,
         FilterOperator.Gte, FilterOperator.Lte, FilterOperator.Between];

    public static readonly string[] ImageFits = ["contain", "cover", "fill"];
    public const int MaxTabs = 10;
    public const int MinReloadSeconds = 10;
    public const int MaxReloadSeconds = 3600;
    public const int MaxTabNameLength = 60;
    public static readonly string[] TextAligns = ["left", "center", "right"];
    public static readonly string[] TextSizes = ["small", "normal", "large"];
    public const int MaxTextLength = 4000;
    public const int MaxAltLength = 200;
    public const int MaxLinkLength = 500;

    /// <param name="existing">
    /// Widgets the dashboard already has, by id. If one of their charts has since been deleted it is simply dropped; a new
    /// widget that names a missing chart is an error. An image widget can only be one of these: pictures are added
    /// through their own call, and its picture is always the one it already has.
    /// </param>
    public static async Task<DashboardDefinition> NormalizeAsync(
        DashboardDefinition? definition, IChartRepository charts, Guid organizationId,
        IReadOnlyDictionary<Guid, DashboardWidget> existing, CancellationToken ct)
    {
        definition ??= new DashboardDefinition([], []);
        var errors = new Dictionary<string, string[]>();
        void Fail(string field, string message) => errors[field] = [message];

        var widgets = definition.Widgets ?? [];
        var filters = definition.Filters ?? [];
        if (widgets.Count > MaxWidgets) Fail("Widgets", $"A dashboard can have at most {MaxWidgets} widgets.");
        if (filters.Count > MaxFilters) Fail("Filters", $"A dashboard can have at most {MaxFilters} filters.");
        if (errors.Count > 0) throw new ValidationException(errors);

        var normalizedFilters = new List<DashboardFilter>();
        for (var i = 0; i < filters.Count; i++)
        {
            var f = filters[i];
            var field = $"Filters[{i}]";
            if (f is null || string.IsNullOrWhiteSpace(f.Label) || f.Label.Trim().Length > MaxTitleLength)
                Fail(field, "A filter needs a label of at most 200 characters.");
            else if (string.IsNullOrWhiteSpace(f.Column))
                Fail(field, "A filter needs a column.");
            else if (!Enum.IsDefined(f.Target))
                Fail(field, "A filter acts on a column or on a parameter.");
            else if (!SharedFilterOperators.Contains(f.Operator))
                Fail(field, $"Operator {f.Operator} is not supported for dashboard filters.");
            else if (f.Target == FilterTarget.Parameter && f.Operator != FilterOperator.Eq)
                Fail(field, "A filter on a parameter sets one value, so its condition is 'is' (Eq).");
            else
                normalizedFilters.Add(f with { Id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id, Label = f.Label.Trim(), Column = f.Column.Trim() });
        }
        if (normalizedFilters.Select(f => f.Id).Distinct().Count() != normalizedFilters.Count)
            Fail("Filters", "Filter ids must be unique.");

        if (definition.ReloadSeconds is < MinReloadSeconds or > MaxReloadSeconds)
            Fail("ReloadSeconds", $"A dashboard reloads every {MinReloadSeconds} to {MaxReloadSeconds} seconds.");

        // Tabs first: a widget's place is checked against them. Without tabs the dashboard is a single page.
        var tabs = definition.Tabs ?? [];
        if (tabs.Count > MaxTabs) Fail("Tabs", $"A dashboard can have at most {MaxTabs} tabs.");
        var normalizedTabs = new List<DashboardTab>();
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            if (tab is null || string.IsNullOrWhiteSpace(tab.Name) || tab.Name.Trim().Length > MaxTabNameLength)
                Fail($"Tabs[{i}]", $"A tab needs a name of at most {MaxTabNameLength} characters.");
            else
                normalizedTabs.Add(tab with { Id = tab.Id == Guid.Empty ? Guid.NewGuid() : tab.Id, Name = tab.Name.Trim() });
        }
        if (normalizedTabs.Select(t => t.Id).Distinct().Count() != normalizedTabs.Count)
            Fail("Tabs", "Tab ids must be unique.");
        if (normalizedTabs.Select(t => t.Name.ToLowerInvariant()).Distinct().Count() != normalizedTabs.Count)
            Fail("Tabs", "Two tabs cannot have the same name.");

        var normalizedWidgets = new List<DashboardWidget>();
        for (var i = 0; i < widgets.Count; i++)
        {
            var w = widgets[i];
            var field = $"Widgets[{i}]";
            if (w is null)
            {
                Fail(field, "A widget needs a chart.");
                continue;
            }

            if (w.Kind == WidgetKind.Image)
            {
                // A picture is made by its own call; here only how it is shown can change.
                if (w.Id == Guid.Empty || !existing.TryGetValue(w.Id, out var current) || current.Kind != WidgetKind.Image || current.ImageKey is null)
                {
                    Fail(field, "A picture is added with its own call, not with the dashboard.");
                    continue;
                }
                if (w.ImageFit is not null && !ImageFits.Contains(w.ImageFit))
                {
                    Fail(field, $"A picture is shown as: {string.Join(", ", ImageFits)}.");
                    continue;
                }
                if (w.Alt is { Length: > MaxAltLength })
                {
                    Fail(field, $"The description of a picture can be at most {MaxAltLength} characters.");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(w.LinkUrl) && !IsWebAddress(w.LinkUrl))
                {
                    Fail(field, "A link must be an address starting with http:// or https://.");
                    continue;
                }
                if (w.Layout is { } imageLayout && LayoutProblem(imageLayout) is { } imageProblem)
                {
                    Fail(field, imageProblem);
                    continue;
                }

                normalizedWidgets.Add(new DashboardWidget(
                    w.Id, Guid.Empty, w.Layout, WidgetKind.Image, current.ImageKey, w.ImageFit ?? current.ImageFit ?? "contain",
                    string.IsNullOrWhiteSpace(w.Alt) ? null : w.Alt.Trim(), string.IsNullOrWhiteSpace(w.LinkUrl) ? null : w.LinkUrl.Trim(),
                    TabId: w.TabId));
                continue;
            }

            if (w.Kind == WidgetKind.Text)
            {
                // Words need no upload: a text widget can be made by saving the dashboard.
                if (string.IsNullOrWhiteSpace(w.Text))
                {
                    Fail(field, "A text widget needs some text.");
                    continue;
                }
                if (w.Text.Length > MaxTextLength)
                {
                    Fail(field, $"A text widget can hold at most {MaxTextLength} characters.");
                    continue;
                }
                if (w.TextAlign is not null && !TextAligns.Contains(w.TextAlign))
                {
                    Fail(field, $"Text is aligned: {string.Join(", ", TextAligns)}.");
                    continue;
                }
                if (w.TextSize is not null && !TextSizes.Contains(w.TextSize))
                {
                    Fail(field, $"Text is sized: {string.Join(", ", TextSizes)}.");
                    continue;
                }
                if (w.Layout is { } textLayout && LayoutProblem(textLayout) is { } textProblem)
                {
                    Fail(field, textProblem);
                    continue;
                }

                normalizedWidgets.Add(new DashboardWidget(
                    w.Id == Guid.Empty ? Guid.NewGuid() : w.Id, Guid.Empty, w.Layout, WidgetKind.Text,
                    Text: w.Text.Trim(), TextAlign: w.TextAlign ?? "left", TextSize: w.TextSize ?? "normal", TabId: w.TabId));
                continue;
            }

            if (w.ChartId == Guid.Empty)
            {
                Fail(field, "A widget needs a chart.");
                continue;
            }
            if (await charts.GetAsync(organizationId, w.ChartId, ct) is null)
            {
                if (w.Id != Guid.Empty && existing.ContainsKey(w.Id)) continue; // its chart was deleted
                Fail(field, "The chart was not found.");
                continue;
            }

            if (w.Layout is { } layout && LayoutProblem(layout) is { } problem)
            {
                Fail(field, problem);
                continue;
            }

            // A chart widget carries nothing of a picture, whatever was sent.
            normalizedWidgets.Add(new DashboardWidget(w.Id == Guid.Empty ? Guid.NewGuid() : w.Id, w.ChartId, w.Layout, TabId: w.TabId));
        }
        if (normalizedWidgets.Select(w => w.Id).Distinct().Count() != normalizedWidgets.Count)
            Fail("Widgets", "Widget ids must be unique.");
        var chartWidgets = normalizedWidgets.Where(w => w.Kind == WidgetKind.Chart).ToList();
        if (chartWidgets.Select(w => w.ChartId).Distinct().Count() != chartWidgets.Count)
            Fail("Widgets", "A chart can only be on a dashboard once.");

        if (errors.Count > 0) throw new ValidationException(errors);

        // Every widget is on a tab that exists: one that names a tab that is not there (a tab that was removed) goes to
        // the first. With no tabs, no widget names one.
        var firstTab = normalizedTabs.Count > 0 ? normalizedTabs[0].Id : (Guid?)null;
        var known = normalizedTabs.Select(t => t.Id).ToHashSet();
        var placed = normalizedWidgets.Select(w => w with { TabId = w.TabId is { } id && known.Contains(id) ? id : firstTab }).ToList();
        return new DashboardDefinition(placed, normalizedFilters, normalizedTabs.Count > 0 ? normalizedTabs : null, definition.ReloadSeconds);
    }

    internal static bool IsWebAddress(string value) =>
        value.Length <= MaxLinkLength && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    /// <summary>Widgets sit on a 12-column grid. Overlaps are not rejected: the client lays out and pushes widgets apart.</summary>
    private static string? LayoutProblem(WidgetLayout l)
    {
        if (l.W < 1 || l.W > GridColumns) return $"A widget is between 1 and {GridColumns} columns wide.";
        if (l.X < 0 || l.X + l.W > GridColumns) return $"A widget has to fit inside the {GridColumns} columns.";
        if (l.H < 1 || l.H > MaxWidgetHeight) return $"A widget is between 1 and {MaxWidgetHeight} rows high.";
        if (l.Y < 0 || l.Y > MaxRow) return "A widget's row is out of range.";
        return null;
    }
}

public sealed class CreateDashboardHandler(
    IDashboardRepository dashboards, IChartRepository charts, IFolderRepository folders, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access,
    DashboardVisibility visibility)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, SaveDashboardRequest request, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();

        if (await dashboards.NameExistsAsync(organizationId, request.Name?.Trim() ?? string.Empty, null, ct))
            throw new ConflictException("A dashboard with that name already exists.");

        await FolderRules.RequireFolderAsync(folders, organizationId, FolderKind.Dashboard, request.FolderId, "FolderId", ct);
        await visibility.RequireFolderAsync(member, organizationId, request.FolderId, "FolderId", ct);
        var definition = await DashboardValidator.NormalizeAsync(request.Definition, charts, organizationId, new Dictionary<Guid, DashboardWidget>(), ct);
        var dashboard = DatasetQuery.Guard(() => Dashboard.Create(
            organizationId, request.Name!, request.Description, DashboardJson.Serialize(definition), userId, clock.UtcNow, request.FolderId));

        await dashboards.AddAsync(dashboard, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return dashboard.ToDto();
    }
}

public sealed class ListDashboardsHandler(IDashboardRepository dashboards, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<IReadOnlyList<DashboardSummaryDto>> HandleAsync(Guid organizationId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var items = await visibility.FilterAsync(member, await dashboards.ListByOrganizationAsync(organizationId, ct), ct);
        return items
            .Select(d => new DashboardSummaryDto(
                d.Id, d.Name, d.Description, DashboardJson.Deserialize(d.DefinitionJson).Widgets.Count(w => w.Kind == WidgetKind.Chart), d.UpdatedUtc,
                DashboardJson.Deserialize(d.DefinitionJson).Widgets.Where(w => w.Kind == WidgetKind.Chart).Select(w => w.ChartId).ToList(), d.FolderId, !d.Access.IsOpen))
            .ToList();
    }
}

public sealed class GetDashboardHandler(IDashboardRepository dashboards, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var dashboard = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);
        return dashboard.ToDto();
    }
}

public sealed class UpdateDashboardHandler(
    IDashboardRepository dashboards, IChartRepository charts, IImageStore images, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, Guid dashboardId, SaveDashboardRequest request, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var dashboard = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);
        if (await dashboards.NameExistsAsync(organizationId, request.Name?.Trim() ?? string.Empty, dashboard.Id, ct))
            throw new ConflictException("A dashboard with that name already exists.");

        var before = DashboardJson.Deserialize(dashboard.DefinitionJson);
        var existing = before.Widgets.ToDictionary(w => w.Id);
        var definition = await DashboardValidator.NormalizeAsync(request.Definition, charts, organizationId, existing, ct);
        DatasetQuery.Guard(() =>
        {
            dashboard.Update(request.Name!, request.Description, DashboardJson.Serialize(definition), clock.UtcNow);
            return true;
        });

        await unitOfWork.SaveChangesAsync(ct);

        // A picture whose widget is gone is not wanted any more.
        var kept = definition.Widgets.Select(w => w.ImageKey).Where(k => k is not null).ToHashSet();
        foreach (var key in before.Widgets.Select(w => w.ImageKey).Where(k => k is not null && !kept.Contains(k)))
            images.Delete(key!);
        return dashboard.ToDto();
    }
}

public sealed class DeleteDashboardHandler(IDashboardRepository dashboards, IImageStore images, IUnitOfWork unitOfWork, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task HandleAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var dashboard = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);
        var background = dashboard.BackgroundImageKey;
        var pictures = DashboardJson.Deserialize(dashboard.DefinitionJson).Widgets.Select(w => w.ImageKey).Where(k => k is not null).ToList();
        dashboards.Remove(dashboard);
        await unitOfWork.SaveChangesAsync(ct);
        if (background is not null) images.Delete(background);
        foreach (var key in pictures) images.Delete(key!);
    }
}

/// <summary>
/// Runs every widget with the viewer's shared-filter values added to its own filters. All the normal chart-query
/// rules apply to each widget (planner, validated SQL, read-only execution). One broken widget does not fail the page.
/// </summary>
public sealed class RunDashboardHandler(
    IDashboardRepository dashboards, IChartRepository charts, IDatasetRepository datasets, RunChartQueryHandler runner, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<DashboardRunResult> HandleAsync(
        Guid organizationId, Guid dashboardId, RunDashboardRequest? request, CancellationToken ct = default)
    {
        // Anyone may look; asking the databases again (instead of reusing stored results) is for people who change the dashboard.
        var refresh = request?.Refresh == true;
        var member = await access.RequireRoleAsync(organizationId, refresh ? OrganizationRole.Editor : OrganizationRole.Viewer, ct);
        var dashboard = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);
        // A dashboard on a schedule keeps what a refresh fetches for as long as the schedule would, so the next viewer gets it too.
        TimeSpan? keepFor = dashboard.RefreshIntervalMinutes is { } minutes ? ScheduledKeepTime(minutes) : null;
        return await RunWidgetsAsync(
            organizationId, DashboardJson.Deserialize(dashboard.DefinitionJson), request,
            Caller.Member, refresh ? new CacheRequest(CacheMode.Refresh, keepFor) : CacheRequest.Use, ct);
    }

    /// <summary>
    /// Runs a dashboard for someone who holds a valid share link (checked by the caller). The widgets run exactly as
    /// for a member, except that no SQL ever comes back.
    /// </summary>
    internal Task<DashboardRunResult> HandleSharedAsync(
        Guid organizationId, DashboardDefinition definition, RunDashboardRequest? request, CancellationToken ct) =>
        RunWidgetsAsync(organizationId, definition, request, Caller.Visitor, CacheRequest.Use, ct); // a visitor cannot force a refresh

    /// <summary>
    /// The scheduled refresh: runs every chart as a viewer would see the dashboard before touching any filter, fetches
    /// fresh data from the databases, and stores it so that the viewers who follow are served it. Nobody is signed in,
    /// so this does not check a role; it is only reached by the scheduler, for a dashboard an editor put on a schedule.
    /// </summary>
    internal Task<DashboardRunResult> HandleScheduledAsync(
        Guid organizationId, DashboardDefinition definition, TimeSpan keepFor, CancellationToken ct) =>
        RunWidgetsAsync(organizationId, definition, null, Caller.Scheduler, new CacheRequest(CacheMode.Refresh, keepFor), ct);

    /// <summary>
    /// How long a refresh's results are kept: two intervals, so that viewers are still served stored data if the next
    /// refresh is a little late (or fails), rather than every one of them suddenly asking the database.
    /// </summary>
    internal static TimeSpan ScheduledKeepTime(int intervalMinutes) => TimeSpan.FromMinutes(intervalMinutes * 2L);

    private const string SharedFailure = "This chart could not be shown.";

    /// <summary>Who the run is for. It decides whether a role is needed, and how much of a failure may be told.</summary>
    private enum Caller
    {
        /// <summary>A signed-in member: checked by the caller, and told what went wrong.</summary>
        Member,

        /// <summary>Someone with a share link: not a member, so told nothing about tables or columns.</summary>
        Visitor,

        /// <summary>The scheduler: no one is signed in; failures are recorded for the dashboard's members.</summary>
        Scheduler,
    }

    private async Task<DashboardRunResult> RunWidgetsAsync(
        Guid organizationId, DashboardDefinition definition, RunDashboardRequest? request, Caller caller, CacheRequest cache, CancellationToken ct)
    {
        var shared = caller == Caller.Visitor; // what a failure may reveal
        var trusted = caller != Caller.Member; // no signed-in member to check
        var chosen = request?.Filters ?? new Dictionary<Guid, IReadOnlyList<JsonElement>>();
        var active = definition.Filters.Where(f => chosen.TryGetValue(f.Id, out var v) && v.Count > 0).ToList();

        var results = new List<WidgetResult>();
        foreach (var widget in definition.Widgets)
        {
            if (widget.Kind != WidgetKind.Chart) continue; // a picture has nothing to run

            var chart = await charts.GetAsync(organizationId, widget.ChartId, ct);
            if (chart is null)
            {
                results.Add(new WidgetResult(widget.Id, null, "This chart was deleted.", []));
                continue;
            }

            var query = ChartDefinition.FromJson(chart.DefinitionJson).Query;
            var dataset = await datasets.GetAsync(organizationId, chart.DatasetId, ct);
            if (dataset is null)
            {
                results.Add(new WidgetResult(widget.Id, null, "The dataset for this chart no longer exists.", []));
                continue;
            }

            var applied = new List<Guid>();
            var filters = (query.Filters ?? []).ToList();
            var parameters = new Dictionary<string, JsonElement>(query.Parameters ?? new Dictionary<string, JsonElement>(), StringComparer.OrdinalIgnoreCase);
            var declared = DatasetParametersJson.FromJson(dataset.ParametersJson);
            foreach (var filter in active)
            {
                var values = chosen[filter.Id];

                if (filter.Target == FilterTarget.Parameter)
                {
                    // Sets a value the dataset's SQL takes, for the charts whose dataset has a parameter of that name.
                    var parameter = declared.FirstOrDefault(p => string.Equals(p.Name, filter.Column, StringComparison.OrdinalIgnoreCase));
                    if (parameter is null) continue;
                    parameters[parameter.Name] = values[0];
                    applied.Add(filter.Id);
                    continue;
                }

                if (!dataset.Columns.Any(c => c.Name == filter.Column)) continue;
                filters.Add(filter.Operator is FilterOperator.In or FilterOperator.Between
                    ? new FilterSpec(filter.Column, filter.Operator, Values: values)
                    : new FilterSpec(filter.Column, filter.Operator, values[0]));
                applied.Add(filter.Id);
            }

            try
            {
                var run = query with { Filters = filters, Parameters = parameters.Count > 0 ? parameters : null };
                var result = trusted
                    ? await runner.HandleTrustedAsync(organizationId, chart.DatasetId, run, cache, ct)
                    : await runner.HandleAsync(organizationId, chart.DatasetId, run, cache, ct);
                results.Add(new WidgetResult(widget.Id, result, null, applied));
            }
            catch (ValidationException ex)
            {
                // Table and column names in these messages are for the dashboard's members, not for the public.
                results.Add(new WidgetResult(widget.Id, null, shared ? SharedFailure : string.Join(" ", ex.Errors.SelectMany(e => e.Value)), applied));
            }
            catch (NotFoundException ex)
            {
                results.Add(new WidgetResult(widget.Id, null, shared ? SharedFailure : ex.Message, applied));
            }
        }

        return new DashboardRunResult(results);
    }
}
