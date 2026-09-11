using System.Text.Json;
using System.Text.Json.Serialization;
using DataBoard.Application.Abstractions;
using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Dashboards;
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
        new(d.Id, d.Name, d.Description, DashboardJson.Deserialize(d.DefinitionJson), d.CreatedUtc, d.UpdatedUtc);
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

    /// <param name="existingWidgetIds">
    /// Widgets the dashboard already has. If one of their charts has since been deleted it is simply dropped; a new
    /// widget that names a missing chart is an error.
    /// </param>
    public static async Task<DashboardDefinition> NormalizeAsync(
        DashboardDefinition? definition, IChartRepository charts, Guid organizationId, IReadOnlySet<Guid> existingWidgetIds, CancellationToken ct)
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
            else if (!SharedFilterOperators.Contains(f.Operator))
                Fail(field, $"Operator {f.Operator} is not supported for dashboard filters.");
            else
                normalizedFilters.Add(f with { Id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id, Label = f.Label.Trim(), Column = f.Column.Trim() });
        }
        if (normalizedFilters.Select(f => f.Id).Distinct().Count() != normalizedFilters.Count)
            Fail("Filters", "Filter ids must be unique.");

        var normalizedWidgets = new List<DashboardWidget>();
        for (var i = 0; i < widgets.Count; i++)
        {
            var w = widgets[i];
            var field = $"Widgets[{i}]";
            if (w is null || w.ChartId == Guid.Empty)
            {
                Fail(field, "A widget needs a chart.");
                continue;
            }
            if (await charts.GetAsync(organizationId, w.ChartId, ct) is null)
            {
                if (w.Id != Guid.Empty && existingWidgetIds.Contains(w.Id)) continue; // its chart was deleted
                Fail(field, "The chart was not found.");
                continue;
            }

            if (w.Layout is { } layout && LayoutProblem(layout) is { } problem)
            {
                Fail(field, problem);
                continue;
            }

            normalizedWidgets.Add(w with { Id = w.Id == Guid.Empty ? Guid.NewGuid() : w.Id });
        }
        if (normalizedWidgets.Select(w => w.Id).Distinct().Count() != normalizedWidgets.Count)
            Fail("Widgets", "Widget ids must be unique.");
        if (normalizedWidgets.Select(w => w.ChartId).Distinct().Count() != normalizedWidgets.Count)
            Fail("Widgets", "A chart can only be on a dashboard once.");

        if (errors.Count > 0) throw new ValidationException(errors);
        return new DashboardDefinition(normalizedWidgets, normalizedFilters);
    }

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
    IDashboardRepository dashboards, IChartRepository charts, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, SaveDashboardRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();

        if (await dashboards.NameExistsAsync(organizationId, request.Name?.Trim() ?? string.Empty, null, ct))
            throw new ConflictException("A dashboard with that name already exists.");

        var definition = await DashboardValidator.NormalizeAsync(request.Definition, charts, organizationId, new HashSet<Guid>(), ct);
        var dashboard = DatasetQuery.Guard(() => Dashboard.Create(
            organizationId, request.Name!, request.Description, DashboardJson.Serialize(definition), userId, clock.UtcNow));

        await dashboards.AddAsync(dashboard, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return dashboard.ToDto();
    }
}

public sealed class ListDashboardsHandler(IDashboardRepository dashboards, OrganizationAccess access)
{
    public async Task<IReadOnlyList<DashboardSummaryDto>> HandleAsync(Guid organizationId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var items = await dashboards.ListByOrganizationAsync(organizationId, ct);
        return items
            .Select(d => new DashboardSummaryDto(
                d.Id, d.Name, d.Description, DashboardJson.Deserialize(d.DefinitionJson).Widgets.Count, d.UpdatedUtc,
                DashboardJson.Deserialize(d.DefinitionJson).Widgets.Select(w => w.ChartId).ToList()))
            .ToList();
    }
}

public sealed class GetDashboardHandler(IDashboardRepository dashboards, OrganizationAccess access)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var dashboard = await dashboards.GetAsync(organizationId, dashboardId, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        return dashboard.ToDto();
    }
}

public sealed class UpdateDashboardHandler(
    IDashboardRepository dashboards, IChartRepository charts, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, Guid dashboardId, SaveDashboardRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var dashboard = await dashboards.GetAsync(organizationId, dashboardId, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        if (await dashboards.NameExistsAsync(organizationId, request.Name?.Trim() ?? string.Empty, dashboard.Id, ct))
            throw new ConflictException("A dashboard with that name already exists.");

        var existing = DashboardJson.Deserialize(dashboard.DefinitionJson).Widgets.Select(w => w.Id).ToHashSet();
        var definition = await DashboardValidator.NormalizeAsync(request.Definition, charts, organizationId, existing, ct);
        DatasetQuery.Guard(() =>
        {
            dashboard.Update(request.Name!, request.Description, DashboardJson.Serialize(definition), clock.UtcNow);
            return true;
        });

        await unitOfWork.SaveChangesAsync(ct);
        return dashboard.ToDto();
    }
}

public sealed class DeleteDashboardHandler(IDashboardRepository dashboards, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task HandleAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var dashboard = await dashboards.GetAsync(organizationId, dashboardId, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        dashboards.Remove(dashboard);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Runs every widget with the viewer's shared-filter values added to its own filters. All the normal chart-query
/// rules apply to each widget (planner, validated SQL, read-only execution). One broken widget does not fail the page.
/// </summary>
public sealed class RunDashboardHandler(
    IDashboardRepository dashboards, IChartRepository charts, IDatasetRepository datasets, RunChartQueryHandler runner, OrganizationAccess access)
{
    public async Task<DashboardRunResult> HandleAsync(
        Guid organizationId, Guid dashboardId, RunDashboardRequest? request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var dashboard = await dashboards.GetAsync(organizationId, dashboardId, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        return await RunWidgetsAsync(organizationId, DashboardJson.Deserialize(dashboard.DefinitionJson), request, shared: false, ct);
    }

    /// <summary>
    /// Runs a dashboard for someone who holds a valid share link (checked by the caller). The widgets run exactly as
    /// for a member, except that no SQL ever comes back.
    /// </summary>
    internal Task<DashboardRunResult> HandleSharedAsync(
        Guid organizationId, DashboardDefinition definition, RunDashboardRequest? request, CancellationToken ct) =>
        RunWidgetsAsync(organizationId, definition, request, shared: true, ct);

    private const string SharedFailure = "This chart could not be shown.";

    private async Task<DashboardRunResult> RunWidgetsAsync(
        Guid organizationId, DashboardDefinition definition, RunDashboardRequest? request, bool shared, CancellationToken ct)
    {
        var chosen = request?.Filters ?? new Dictionary<Guid, IReadOnlyList<JsonElement>>();
        var active = definition.Filters.Where(f => chosen.TryGetValue(f.Id, out var v) && v.Count > 0).ToList();

        var results = new List<WidgetResult>();
        foreach (var widget in definition.Widgets)
        {
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
            foreach (var filter in active)
            {
                if (!dataset.Columns.Any(c => c.Name == filter.Column)) continue;
                var values = chosen[filter.Id];
                filters.Add(filter.Operator is FilterOperator.In or FilterOperator.Between
                    ? new FilterSpec(filter.Column, filter.Operator, Values: values)
                    : new FilterSpec(filter.Column, filter.Operator, values[0]));
                applied.Add(filter.Id);
            }

            try
            {
                var run = query with { Filters = filters };
                var result = shared
                    ? await runner.HandleTrustedAsync(organizationId, chart.DatasetId, run, ct)
                    : await runner.HandleAsync(organizationId, chart.DatasetId, run, ct);
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
