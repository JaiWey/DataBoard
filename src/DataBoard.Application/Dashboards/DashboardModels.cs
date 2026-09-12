using System.Text.Json;
using DataBoard.Application.Charts;

namespace DataBoard.Application.Dashboards;

/// <summary>
/// Where a widget sits on the dashboard's grid, in grid cells: <paramref name="X"/> is the column (0-based) and
/// <paramref name="Y"/> the row, <paramref name="W"/> and <paramref name="H"/> the width in columns and the height in rows.
/// </summary>
public sealed record WidgetLayout(int X, int Y, int W, int H);

/// <summary>
/// One chart placed on a dashboard. The chart itself (dataset, type, query, style) is a saved chart, shared by every
/// dashboard that shows it. A widget without a <paramref name="Layout"/> (older dashboards) is placed by the client.
/// </summary>
public sealed record DashboardWidget(Guid Id, Guid ChartId, WidgetLayout? Layout = null);

/// <summary>
/// A filter shared by the whole dashboard. Viewers pick values; the filter then applies to every widget whose dataset
/// has a column with exactly this name, and widgets without that column ignore it.
/// </summary>
public sealed record DashboardFilter(Guid Id, string Label, string Column, FilterOperator Operator, FilterTarget Target = FilterTarget.Column);

/// <summary>What a shared filter acts on: a dataset column (it filters rows), or a dataset parameter (it sets a value the SQL takes).</summary>
public enum FilterTarget { Column = 0, Parameter = 1 }

public sealed record DashboardDefinition(IReadOnlyList<DashboardWidget> Widgets, IReadOnlyList<DashboardFilter> Filters);

public sealed record SaveDashboardRequest(string Name, string? Description, DashboardDefinition? Definition);

/// <param name="ChartIds">The charts on the dashboard, so a chart can show which dashboards use it.</param>
public sealed record DashboardSummaryDto(
    Guid Id, string Name, string? Description, int WidgetCount, DateTime UpdatedUtc, IReadOnlyList<Guid>? ChartIds = null);

public sealed record DashboardDto(
    Guid Id, string Name, string? Description, DashboardDefinition Definition, DateTime CreatedUtc, DateTime UpdatedUtc);

/// <param name="Filters">Filter id -> the values the viewer picked. A filter with no values is not applied.</param>
public sealed record RunDashboardRequest(IReadOnlyDictionary<Guid, IReadOnlyList<JsonElement>>? Filters = null);

/// <param name="AppliedFilters">Ids of the shared filters that were applied to this widget.</param>
public sealed record WidgetResult(
    Guid WidgetId, ChartResult? Result, string? Error, IReadOnlyList<Guid> AppliedFilters);

public sealed record DashboardRunResult(IReadOnlyList<WidgetResult> Widgets);
