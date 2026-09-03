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
/// Checks a dashboard definition before it is saved: sizes, ids, filter operators, and that every widget's query
/// plans cleanly against its dataset. A dashboard that could never run is rejected up front, with the widget named.
/// </summary>
internal static class DashboardValidator
{
    public const int MaxWidgets = 24;
    public const int MaxFilters = 10;
    public const int MaxTitleLength = 200;

    private static readonly FilterOperator[] SharedFilterOperators =
        [FilterOperator.Eq, FilterOperator.NotEq, FilterOperator.In, FilterOperator.Contains, FilterOperator.StartsWith,
         FilterOperator.Gte, FilterOperator.Lte, FilterOperator.Between];

    public static async Task<DashboardDefinition> NormalizeAsync(
        DashboardDefinition? definition, IDatasetRepository datasets, Guid organizationId, CancellationToken ct)
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

        var cache = new Dictionary<Guid, Domain.Datasets.Dataset?>();
        var normalizedWidgets = new List<DashboardWidget>();
        for (var i = 0; i < widgets.Count; i++)
        {
            var w = widgets[i];
            var field = $"Widgets[{i}]";
            if (w is null || string.IsNullOrWhiteSpace(w.Title) || w.Title.Trim().Length > MaxTitleLength)
            {
                Fail(field, "A widget needs a title of at most 200 characters.");
                continue;
            }
            if (!Enum.IsDefined(w.ChartType))
            {
                Fail(field, "Unknown chart type.");
                continue;
            }
            if (!cache.TryGetValue(w.DatasetId, out var dataset))
                cache[w.DatasetId] = dataset = await datasets.GetAsync(organizationId, w.DatasetId, ct);
            if (dataset is null)
            {
                Fail(field, $"The dataset for '{w.Title.Trim()}' was not found.");
                continue;
            }

            try
            {
                ChartQueryPlanner.Plan(dataset.Columns, w.Query ?? new ChartQuery());
            }
            catch (ValidationException ex)
            {
                Fail(field, $"'{w.Title.Trim()}': " + string.Join(" ", ex.Errors.SelectMany(e => e.Value)));
                continue;
            }

            normalizedWidgets.Add(w with { Id = w.Id == Guid.Empty ? Guid.NewGuid() : w.Id, Title = w.Title.Trim(), Query = w.Query ?? new ChartQuery() });
        }
        if (normalizedWidgets.Select(w => w.Id).Distinct().Count() != normalizedWidgets.Count)
            Fail("Widgets", "Widget ids must be unique.");

        if (errors.Count > 0) throw new ValidationException(errors);
        return new DashboardDefinition(normalizedWidgets, normalizedFilters);
    }
}

public sealed class CreateDashboardHandler(
    IDashboardRepository dashboards, IDatasetRepository datasets, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, SaveDashboardRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();

        if (await dashboards.NameExistsAsync(organizationId, request.Name?.Trim() ?? string.Empty, null, ct))
            throw new ConflictException("A dashboard with that name already exists.");

        var definition = await DashboardValidator.NormalizeAsync(request.Definition, datasets, organizationId, ct);
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
                d.Id, d.Name, d.Description, DashboardJson.Deserialize(d.DefinitionJson).Widgets.Count, d.UpdatedUtc))
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
    IDashboardRepository dashboards, IDatasetRepository datasets, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, Guid dashboardId, SaveDashboardRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var dashboard = await dashboards.GetAsync(organizationId, dashboardId, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        if (await dashboards.NameExistsAsync(organizationId, request.Name?.Trim() ?? string.Empty, dashboard.Id, ct))
            throw new ConflictException("A dashboard with that name already exists.");

        var definition = await DashboardValidator.NormalizeAsync(request.Definition, datasets, organizationId, ct);
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
    IDashboardRepository dashboards, IDatasetRepository datasets, RunChartQueryHandler runner, OrganizationAccess access)
{
    public async Task<DashboardRunResult> HandleAsync(
        Guid organizationId, Guid dashboardId, RunDashboardRequest? request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var dashboard = await dashboards.GetAsync(organizationId, dashboardId, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        var definition = DashboardJson.Deserialize(dashboard.DefinitionJson);

        var chosen = request?.Filters ?? new Dictionary<Guid, IReadOnlyList<JsonElement>>();
        var active = definition.Filters.Where(f => chosen.TryGetValue(f.Id, out var v) && v.Count > 0).ToList();

        var results = new List<WidgetResult>();
        foreach (var widget in definition.Widgets)
        {
            var dataset = await datasets.GetAsync(organizationId, widget.DatasetId, ct);
            if (dataset is null)
            {
                results.Add(new WidgetResult(widget.Id, null, "The dataset for this widget no longer exists.", []));
                continue;
            }

            var applied = new List<Guid>();
            var filters = (widget.Query.Filters ?? []).ToList();
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
                var result = await runner.HandleAsync(organizationId, widget.DatasetId, widget.Query with { Filters = filters }, ct);
                results.Add(new WidgetResult(widget.Id, result, null, applied));
            }
            catch (ValidationException ex)
            {
                results.Add(new WidgetResult(widget.Id, null, string.Join(" ", ex.Errors.SelectMany(e => e.Value)), applied));
            }
            catch (NotFoundException ex)
            {
                results.Add(new WidgetResult(widget.Id, null, ex.Message, applied));
            }
        }

        return new DashboardRunResult(results);
    }
}
