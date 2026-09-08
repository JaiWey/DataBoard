using System.Text.Json;
using DataBoard.Application.Charts;

namespace DataBoard.Application.Dashboards;

/// <summary>One chart placed on a dashboard. The chart itself (dataset, type, query, style) is a saved chart, shared by every dashboard that shows it.</summary>
public sealed record DashboardWidget(Guid Id, Guid ChartId);

/// <summary>
/// A filter shared by the whole dashboard. Viewers pick values; the filter then applies to every widget whose dataset
/// has a column with exactly this name, and widgets without that column ignore it.
/// </summary>
public sealed record DashboardFilter(Guid Id, string Label, string Column, FilterOperator Operator);

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
