using System.ComponentModel;
using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.Dashboards;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Charts;
using DataBoard.Domain.Users;
using DataBoard.Server.Infrastructure;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DataBoard.Server.Mcp;

/// <summary>
/// What an AI assistant can do in DataBoard, through the MCP endpoint. Every tool is a thin wrapper over the same
/// application handler the web app uses, so the token owner's organization roles, the query planner's checks and the
/// read-only database access all apply unchanged. There is deliberately no tool that deletes anything, writes SQL,
/// or creates a public link.
/// </summary>
[McpServerToolType]
public sealed class DataBoardTools(IServiceProvider services, IHttpContextAccessor http, ILogger<DataBoardTools> log)
{
    private T Get<T>() where T : notnull => services.GetRequiredService<T>();

    /// <summary>
    /// Checks the token's scope, runs the tool, and turns every problem the application reports into a message the
    /// assistant can act on (what was wrong, and where) instead of a bare failure.
    /// </summary>
    private async Task<T> Run<T>(string tool, Guid? organizationId, bool writes, Func<Task<T>> body)
    {
        var user = http.HttpContext?.User;
        var scope = user is null ? null : ApiTokenClaims.ScopeOf(user);
        if (scope is null) throw new McpException("This endpoint needs a DataBoard access token.");
        if (writes && scope != ApiTokenScope.Write)
            throw new McpException("This access token is read-only. Create a token with the Write scope to change charts or dashboards.");

        log.LogInformation("MCP tool {Tool} by token {Token} in organization {Organization}",
            tool, user!.FindFirst("token_name")?.Value, organizationId);
        try
        {
            return await body();
        }
        catch (ValidationException ex)
        {
            throw new McpException(string.Join(" ", ex.Errors.SelectMany(e => e.Value.Select(m => e.Key is "" or "Query" ? m : $"{e.Key}: {m}"))), ex);
        }
        catch (Exception ex) when (ex is NotFoundException or ForbiddenException or ConflictException or UnauthorizedException)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    // ---- looking around ---------------------------------------------------------------------------------------

    [McpServerTool(Name = "list_organizations", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists the organizations the token's owner belongs to, with their role (Viewer, Editor or Owner). Everything else needs an organizationId from here. Creating charts and dashboards needs Editor or Owner.")]
    public Task<IReadOnlyList<OrganizationDto>> ListOrganizations(CancellationToken ct) =>
        Run("list_organizations", null, false, () => Get<ListMyOrganizationsHandler>().HandleAsync(ct));

    [McpServerTool(Name = "list_datasets", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists the datasets of an organization with their columns. A chart is built on one dataset and can only use its columns. Each column has a dataType and a role: Dimension (to group by) or Measure (a number to total up).")]
    public Task<IReadOnlyList<DatasetSummary>> ListDatasets(
        [Description("The organization's id, from list_organizations.")] Guid organizationId, CancellationToken ct) =>
        Run("list_datasets", organizationId, false, async () =>
            (IReadOnlyList<DatasetSummary>)(await Get<ListDatasetsHandler>().HandleAsync(organizationId, ct))
                .Select(d => new DatasetSummary(d.Id, d.Name, d.Description, d.Columns, d.Parameters ?? []))
                .ToList());

    [McpServerTool(Name = "preview_chart", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Runs a chart query without saving anything and returns the first rows, so the numbers can be checked before a chart is created. Use it to confirm that a query gives what the user asked for.")]
    public Task<ChartPreview> PreviewChart(
        [Description("The organization's id.")] Guid organizationId,
        [Description("The dataset's id, from list_datasets.")] Guid datasetId,
        [Description(QueryHelp.Query)] ChartQuery query,
        [Description("How many rows to return, 1 to 200. Default 20.")] int maxRows = 20,
        CancellationToken ct = default) =>
        Run("preview_chart", organizationId, false, async () =>
        {
            var result = await Get<RunChartQueryHandler>().HandleAsync(organizationId, datasetId, query, ct);
            var rows = result.Rows.Take(Math.Clamp(maxRows, 1, 200)).ToList();
            return new ChartPreview(result.Columns.Select(c => new PreviewColumnInfo(c.Name, c.DataType)).ToList(), rows, result.Rows.Count, result.Truncated);
        });

    [McpServerTool(Name = "list_charts", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists the saved charts of an organization: their type, dataset and query. Reuse an existing chart when it already shows what is needed.")]
    public Task<IReadOnlyList<ChartInfo>> ListCharts(
        [Description("The organization's id.")] Guid organizationId, CancellationToken ct) =>
        Run("list_charts", organizationId, false, async () =>
            (IReadOnlyList<ChartInfo>)(await Get<ListChartsHandler>().HandleAsync(organizationId, ct)).Select(ChartInfo.From).ToList());

    [McpServerTool(Name = "list_dashboards", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists the dashboards of an organization and which charts each one holds.")]
    public Task<IReadOnlyList<DashboardSummaryDto>> ListDashboards(
        [Description("The organization's id.")] Guid organizationId, CancellationToken ct) =>
        Run("list_dashboards", organizationId, false, () => Get<ListDashboardsHandler>().HandleAsync(organizationId, ct));

    [McpServerTool(Name = "get_dashboard", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Returns one dashboard: its charts (widgets, with their place on the grid) and its shared filters.")]
    public Task<DashboardInfo> GetDashboard(
        [Description("The organization's id.")] Guid organizationId,
        [Description("The dashboard's id, from list_dashboards.")] Guid dashboardId,
        CancellationToken ct) =>
        Run("get_dashboard", organizationId, false, async () => DashboardInfo.From(await Get<GetDashboardHandler>().HandleAsync(organizationId, dashboardId, ct)));

    // ---- making and changing charts ---------------------------------------------------------------------------

    [McpServerTool(Name = "create_chart", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Saves a new chart on a dataset. The query is checked against the dataset's columns first; if it is wrong the error says what to change. Preview the query first. Needs a Write token and the Editor or Owner role.")]
    public Task<ChartInfo> CreateChart(
        [Description("The organization's id.")] Guid organizationId,
        [Description("The chart's name. Must be unique in the organization.")] string name,
        [Description("The dataset's id, from list_datasets.")] Guid datasetId,
        [Description(QueryHelp.Type)] ChartType type,
        [Description(QueryHelp.Query)] ChartQuery query,
        [Description("A short description. Optional.")] string? description = null,
        [Description(QueryHelp.Style)] ChartStyle? style = null,
        CancellationToken ct = default) =>
        Run("create_chart", organizationId, true, async () =>
            ChartInfo.From(await Get<CreateChartHandler>().HandleAsync(
                organizationId, new SaveChartRequest(name, description, datasetId, type, new ChartDefinition(query, style ?? new ChartStyle())), ct)));

    [McpServerTool(Name = "update_chart", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Replaces everything about an existing chart (name, dataset, type, query, style). Every dashboard that holds the chart shows the change. Send the full new definition, not just what changed; get it from list_charts first. Needs a Write token and the Editor or Owner role.")]
    public Task<ChartInfo> UpdateChart(
        [Description("The organization's id.")] Guid organizationId,
        [Description("The chart's id, from list_charts.")] Guid chartId,
        [Description("The chart's name.")] string name,
        [Description("The dataset's id.")] Guid datasetId,
        [Description(QueryHelp.Type)] ChartType type,
        [Description(QueryHelp.Query)] ChartQuery query,
        [Description("A short description. Optional.")] string? description = null,
        [Description(QueryHelp.Style)] ChartStyle? style = null,
        CancellationToken ct = default) =>
        Run("update_chart", organizationId, true, async () =>
            ChartInfo.From(await Get<UpdateChartHandler>().HandleAsync(
                organizationId, chartId, new SaveChartRequest(name, description, datasetId, type, new ChartDefinition(query, style ?? new ChartStyle())), ct)));

    // ---- making and changing dashboards -----------------------------------------------------------------------

    [McpServerTool(Name = "create_dashboard", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a dashboard from saved charts. The charts are laid out side by side on a 12-column grid, two per row; use set_widget_layout to change a place. Needs a Write token and the Editor or Owner role.")]
    public Task<DashboardInfo> CreateDashboard(
        [Description("The organization's id.")] Guid organizationId,
        [Description("The dashboard's name. Must be unique in the organization.")] string name,
        [Description("The ids of the charts to put on it, from create_chart or list_charts. A chart can be on a dashboard once. At most 24.")] Guid[] chartIds,
        [Description("A short description. Optional.")] string? description = null,
        CancellationToken ct = default) =>
        Run("create_dashboard", organizationId, true, async () =>
        {
            var widgets = chartIds.Distinct().Select(chartId => new DashboardWidget(Guid.Empty, chartId)).ToList();
            var created = await Get<CreateDashboardHandler>().HandleAsync(
                organizationId, new SaveDashboardRequest(name, description, new DashboardDefinition(widgets, [])), ct);
            return DashboardInfo.From(created);
        });

    [McpServerTool(Name = "add_charts_to_dashboard", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Puts more saved charts on an existing dashboard. Charts that are already on it are skipped. Needs a Write token and the Editor or Owner role.")]
    public Task<DashboardInfo> AddChartsToDashboard(
        [Description("The organization's id.")] Guid organizationId,
        [Description("The dashboard's id.")] Guid dashboardId,
        [Description("The ids of the charts to add.")] Guid[] chartIds,
        CancellationToken ct = default) =>
        Run("add_charts_to_dashboard", organizationId, true, () => ChangeDashboard(organizationId, dashboardId, d =>
        {
            var present = d.Widgets.Select(w => w.ChartId).ToHashSet();
            var added = chartIds.Distinct().Where(id => !present.Contains(id)).Select(id => new DashboardWidget(Guid.Empty, id));
            return d with { Widgets = [.. d.Widgets, .. added] };
        }, ct));

    [McpServerTool(Name = "set_widget_layout", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Moves and resizes one chart on a dashboard. The grid has 12 columns and rows about 80px high. x is the column (0 to 11), y the row, w the width in columns (1 to 12, and x + w can be at most 12), h the height in rows (1 to 20). Widgets that end up overlapping are pushed apart when the dashboard is opened. Needs a Write token and the Editor or Owner role.")]
    public Task<DashboardInfo> SetWidgetLayout(
        [Description("The organization's id.")] Guid organizationId,
        [Description("The dashboard's id.")] Guid dashboardId,
        [Description("The id of the chart on the dashboard.")] Guid chartId,
        [Description("Column, 0 to 11.")] int x,
        [Description("Row, 0 or more.")] int y,
        [Description("Width in columns, 1 to 12.")] int w,
        [Description("Height in rows, 1 to 20.")] int h,
        CancellationToken ct = default) =>
        Run("set_widget_layout", organizationId, true, () => ChangeDashboard(organizationId, dashboardId, d =>
        {
            if (!d.Widgets.Any(widget => widget.ChartId == chartId))
                throw new NotFoundException("That chart is not on this dashboard. Add it with add_charts_to_dashboard first.");
            return d with
            {
                Widgets = d.Widgets.Select(widget => widget.ChartId == chartId ? widget with { Layout = new WidgetLayout(x, y, w, h) } : widget).ToList(),
            };
        }, ct));

    [McpServerTool(Name = "add_dashboard_filter", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Adds a shared filter to a dashboard: viewers type a value once and it applies to every chart whose dataset has a column (or, with target Parameter, a parameter) with exactly this name. Needs a Write token and the Editor or Owner role.")]
    public Task<DashboardInfo> AddDashboardFilter(
        [Description("The organization's id.")] Guid organizationId,
        [Description("The dashboard's id.")] Guid dashboardId,
        [Description("The label viewers see, such as \"Region\".")] string label,
        [Description("The dataset column (or parameter, when the target is Parameter) the filter applies to, by its exact name.")] string column,
        [Description("How the typed value is compared: Eq, NotEq, In, Contains, StartsWith, Gte, Lte or Between. A filter on a parameter sets one value, so it is Eq.")] FilterOperator @operator,
        [Description("Column (default) filters rows of the charts. Parameter sets the value of a dataset parameter, for the charts whose dataset has one with that name.")] FilterTarget target = FilterTarget.Column,
        CancellationToken ct = default) =>
        Run("add_dashboard_filter", organizationId, true, () => ChangeDashboard(organizationId, dashboardId,
            d => d with { Filters = [.. d.Filters, new DashboardFilter(Guid.Empty, label, column, @operator, target)] }, ct));

    /// <summary>Reads a dashboard, applies a change to its definition, and saves it through the normal update handler (validation included).</summary>
    private async Task<DashboardInfo> ChangeDashboard(
        Guid organizationId, Guid dashboardId, Func<DashboardDefinition, DashboardDefinition> change, CancellationToken ct)
    {
        var current = await Get<GetDashboardHandler>().HandleAsync(organizationId, dashboardId, ct);
        var saved = await Get<UpdateDashboardHandler>().HandleAsync(
            organizationId, dashboardId, new SaveDashboardRequest(current.Name, current.Description, change(current.Definition)), ct);
        return DashboardInfo.From(saved);
    }
}

// ---- what the tools return: only what an assistant needs, never SQL, connection details or other people's data ------

/// <param name="Parameters">Values the dataset's SQL takes (name, type, default). A chart can set them in its query's "parameters".</param>
public sealed record DatasetSummary(
    Guid Id, string Name, string? Description, IReadOnlyList<DatasetColumnDto> Columns, IReadOnlyList<DatasetParameter> Parameters);

public sealed record PreviewColumnInfo(string Name, string DataType);

/// <param name="RowCount">How many rows the query returned (at most the query's own limit).</param>
public sealed record ChartPreview(IReadOnlyList<PreviewColumnInfo> Columns, IReadOnlyList<object?[]> Rows, int RowCount, bool Truncated);

public sealed record ChartInfo(Guid Id, string Name, string? Description, Guid DatasetId, ChartType Type, ChartQuery Query, ChartStyle Style)
{
    public static ChartInfo From(ChartDto c) => new(c.Id, c.Name, c.Description, c.DatasetId, c.Type, c.Definition.Query, c.Definition.Style ?? new ChartStyle());
}

public sealed record DashboardInfo(Guid Id, string Name, string? Description, IReadOnlyList<DashboardWidget> Widgets, IReadOnlyList<DashboardFilter> Filters)
{
    public static DashboardInfo From(DashboardDto d) => new(d.Id, d.Name, d.Description, d.Definition.Widgets, d.Definition.Filters);
}

/// <summary>The part of the query format an assistant needs, kept next to the tools that take it.</summary>
internal static class QueryHelp
{
    public const string Type = "Table, Bar, BarHorizontal, Line, Area, Pie, Donut, Number, Heatmap or Scatter. Bar, BarHorizontal, Line and Area have an X-axis. Pie and Donut need one dimension and one measure. Number needs exactly one measure and no dimensions. Heatmap needs exactly two dimensions (across, then down) and one measure (the colour). Scatter needs one dimension (what each point is, such as a customer) and two measures (X, then Y); a second dimension splits the points into coloured series and a third measure sizes them (a bubble chart).";

    public const string Query =
        "What to show, as a description (never SQL). dimensions: columns to group by, e.g. {\"column\":\"region\"}; a date or timestamp column can add \"grain\": Day, Week, Month, Quarter or Year. " +
        "On a chart with an axis the first dimension is the X-axis and a second dimension splits the data into one series per value. " +
        "measures: totals to compute, e.g. {\"aggregation\":\"Sum\",\"column\":\"price\",\"alias\":\"revenue\"}; aggregation is Sum, Avg, Min, Max, Count or CountDistinct (Sum and Avg need a number column; Count needs no column and counts rows). " +
        "filters: conditions on columns, e.g. {\"column\":\"region\",\"operator\":\"In\",\"values\":[\"Auckland\"]}; operators Eq, NotEq, Gt, Gte, Lt, Lte (value), In, NotIn, Between (values), Contains, StartsWith (text value), IsNull, IsNotNull. " +
        "orderBy: [{\"field\":\"revenue\",\"direction\":\"Desc\"}] names a dimension or measure alias. limit: rows, 1 to 1000 (default 100). " +
        "seriesLimit: keep only the largest N series (1 to 100) when there are two dimensions. " +
        "parameters: values for the dataset's own parameters (listed by list_datasets), as {\"region\":\"Auckland\"}; a parameter not given uses its default. Use only column names from list_datasets.";

    public const string Style = "How it looks. Optional. legend (true), labels (false: show values on the chart), palette (default, warm, cool or mono), stack (false: pile series on top of each other on bar, line and area charts), percent (false: stack and scale each category to 100%, to compare shares).";
}
