using System.Text.Json;
using DataBoard.Application.Charts;

namespace DataBoard.Application.Dashboards;

/// <summary>
/// Where a widget sits on the dashboard's grid, in grid cells: <paramref name="X"/> is the column (0-based) and
/// <paramref name="Y"/> the row, <paramref name="W"/> and <paramref name="H"/> the width in columns and the height in rows.
/// </summary>
public sealed record WidgetLayout(int X, int Y, int W, int H);

/// <summary>What a widget shows: a saved chart, a picture kept with the dashboard, or a piece of text.</summary>
public enum WidgetKind { Chart = 0, Image = 1, Text = 2 }

/// <summary>
/// One thing placed on a dashboard. A chart widget shows a saved chart (dataset, type, query, style), shared by every
/// dashboard that shows it. An image widget shows a picture that belongs to this dashboard: the picture is uploaded
/// through its own call (the server makes the widget and names the picture), after which only its fit, description and
/// link can be changed here. A widget without a <paramref name="Layout"/> (older dashboards) is placed by the client.
/// </summary>
/// <param name="ChartId">The chart of a chart widget; empty for an image.</param>
/// <param name="ImageKey">Where the picture of an image widget is kept. Set by the server only; whatever a client sends is ignored.</param>
/// <param name="ImageFit">contain (the whole picture), cover (fills the widget, may crop) or fill (stretched).</param>
/// <param name="Alt">What the picture shows, for people who cannot see it.</param>
/// <param name="LinkUrl">Where a click on the picture goes: an http or https address.</param>
/// <param name="Text">The words of a text widget, in simple Markdown (headings, bold, italic, lists, links). Shown by the page, never as HTML.</param>
/// <param name="TextAlign">left, center or right.</param>
/// <param name="TextSize">small, normal or large.</param>
public sealed record DashboardWidget(
    Guid Id, Guid ChartId, WidgetLayout? Layout = null,
    WidgetKind Kind = WidgetKind.Chart, string? ImageKey = null, string? ImageFit = null, string? Alt = null, string? LinkUrl = null,
    string? Text = null, string? TextAlign = null, string? TextSize = null);

/// <summary>
/// A filter shared by the whole dashboard. Viewers pick values; the filter then applies to every widget whose dataset
/// has a column with exactly this name, and widgets without that column ignore it.
/// </summary>
public sealed record DashboardFilter(Guid Id, string Label, string Column, FilterOperator Operator, FilterTarget Target = FilterTarget.Column);

/// <summary>What a shared filter acts on: a dataset column (it filters rows), or a dataset parameter (it sets a value the SQL takes).</summary>
public enum FilterTarget { Column = 0, Parameter = 1 }

public sealed record DashboardDefinition(IReadOnlyList<DashboardWidget> Widgets, IReadOnlyList<DashboardFilter> Filters);

/// <param name="FolderId">Where a new dashboard is filed (create only; moving one is a separate call). Null is the top level.</param>
public sealed record SaveDashboardRequest(string Name, string? Description, DashboardDefinition? Definition, Guid? FolderId = null);

/// <param name="ChartIds">The charts on the dashboard, so a chart can show which dashboards use it.</param>
public sealed record DashboardSummaryDto(
    Guid Id, string Name, string? Description, int WidgetCount, DateTime UpdatedUtc, IReadOnlyList<Guid>? ChartIds = null,
    Guid? FolderId = null);

/// <summary>
/// The refresh schedule of a dashboard and how the last refresh went. Every member sees it (it says how old their data
/// can be); public share links do not.
/// </summary>
/// <param name="IntervalMinutes">How often the server refreshes the charts; null when it does not.</param>
/// <param name="LastFailures">How many charts could not be refreshed the last time; 0 when it went well.</param>
public sealed record DashboardRefreshDto(
    int? IntervalMinutes, DateTime? NextUtc, DateTime? LastUtc, int LastFailures, string? LastMessage);

public sealed record DashboardDto(
    Guid Id, string Name, string? Description, DashboardDefinition Definition, DateTime CreatedUtc, DateTime UpdatedUtc,
    DashboardRefreshDto? Refresh = null, Guid? FolderId = null, DashboardBackgroundDto? Background = null);

/// <param name="IntervalMinutes">5 to 1440, or null to stop refreshing on a schedule.</param>
public sealed record SetDashboardRefreshRequest(int? IntervalMinutes);

/// <param name="Filters">Filter id -> the values the viewer picked. A filter with no values is not applied.</param>
/// <param name="Refresh">Fetch fresh data from the databases instead of reusing stored results, and store it for the next viewer. Editors and Owners only.</param>
public sealed record RunDashboardRequest(IReadOnlyDictionary<Guid, IReadOnlyList<JsonElement>>? Filters = null, bool Refresh = false);

/// <param name="AppliedFilters">Ids of the shared filters that were applied to this widget.</param>
public sealed record WidgetResult(
    Guid WidgetId, ChartResult? Result, string? Error, IReadOnlyList<Guid> AppliedFilters);

public sealed record DashboardRunResult(IReadOnlyList<WidgetResult> Widgets);
