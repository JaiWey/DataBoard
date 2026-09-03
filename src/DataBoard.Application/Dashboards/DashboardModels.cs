using System.Text.Json;
using DataBoard.Application.Charts;

namespace DataBoard.Application.Dashboards;

public enum ChartType { Table, Bar, Line, Pie }

/// <summary>One chart on a dashboard: a title, the dataset it reads, how to draw it, and the query that feeds it.</summary>
public sealed record DashboardWidget(Guid Id, string Title, Guid DatasetId, ChartType ChartType, ChartQuery Query);

/// <summary>
/// A filter shared by the whole dashboard. Viewers pick values; the filter then applies to every widget whose dataset
/// has a column with exactly this name, and widgets without that column ignore it.
/// </summary>
public sealed record DashboardFilter(Guid Id, string Label, string Column, FilterOperator Operator);

public sealed record DashboardDefinition(IReadOnlyList<DashboardWidget> Widgets, IReadOnlyList<DashboardFilter> Filters);

public sealed record SaveDashboardRequest(string Name, string? Description, DashboardDefinition? Definition);

public sealed record DashboardSummaryDto(Guid Id, string Name, string? Description, int WidgetCount, DateTime UpdatedUtc);

public sealed record DashboardDto(
    Guid Id, string Name, string? Description, DashboardDefinition Definition, DateTime CreatedUtc, DateTime UpdatedUtc);

/// <param name="Filters">Filter id -> the values the viewer picked. A filter with no values is not applied.</param>
public sealed record RunDashboardRequest(IReadOnlyDictionary<Guid, IReadOnlyList<JsonElement>>? Filters = null);

/// <param name="AppliedFilters">Ids of the shared filters that were applied to this widget.</param>
public sealed record WidgetResult(
    Guid WidgetId, ChartResult? Result, string? Error, IReadOnlyList<Guid> AppliedFilters);

public sealed record DashboardRunResult(IReadOnlyList<WidgetResult> Widgets);
