using System.Text.Json;
using System.Text.Json.Serialization;
using DataBoard.Domain.Charts;

namespace DataBoard.Application.Charts;

/// <summary>How numbers are written on labels, axes and tooltips, and in a Number chart.</summary>
/// <param name="Decimals">Digits after the point (0 to 6). Null leaves the number as it is.</param>
/// <param name="Thousands">Group digits (1,234,567).</param>
/// <param name="Compact">Short forms: 1.2K, 3.4M.</param>
/// <param name="Prefix">Written before the number, for example a currency sign.</param>
/// <param name="Suffix">Written after the number, for example a unit.</param>
public sealed record NumberFormat(int? Decimals = null, bool Thousands = true, bool Compact = false, string? Prefix = null, string? Suffix = null);

/// <summary>How text is drawn: its font, size, weight, slant and colour. Anything left out is the chart's own.</summary>
/// <param name="Family">One of <see cref="ChartDefinitionValidator.FontFamilies"/>. Null is the default font.</param>
/// <param name="Size">In pixels, 8 to 48.</param>
/// <param name="Weight">normal or bold.</param>
/// <param name="Color">#rgb or #rrggbb.</param>
public sealed record FontStyle(string? Family = null, int? Size = null, string? Weight = null, bool Italic = false, string? Color = null);

/// <summary>How one axis looks: which parts of it show, and the text on it. X is the horizontal axis and Y the vertical one.</summary>
/// <param name="Show">The whole axis. Off hides its line, ticks and labels (its title stays).</param>
/// <param name="LineColor">Colour of the axis line (#rgb or #rrggbb).</param>
/// <param name="LabelFont">The numbers or names along the axis.</param>
/// <param name="TitleFont">The axis title.</param>
public sealed record AxisStyle(
    bool Show = true, bool Line = true, bool Ticks = true, bool Labels = true, string? LineColor = null,
    FontStyle? LabelFont = null, FontStyle? TitleFont = null);

/// <summary>The lines across the plot that belong to one axis. Left out, the axis has the chart's usual ones.</summary>
/// <param name="Show">Null follows the usual (and <c>Grid</c>).</param>
/// <param name="Width">1 to 10.</param>
/// <param name="LineType">solid, dashed or dotted.</param>
public sealed record SplitLineStyle(bool? Show = null, string? Color = null, int? Width = null, string? LineType = null);

/// <summary>The space between the plot and the edge of the chart, in pixels (0 to 300). Left out, it is worked out.</summary>
public sealed record MarginStyle(int? Left = null, int? Top = null, int? Right = null, int? Bottom = null);

/// <summary>How a saved chart is drawn. Only the stored choices; the data comes from the query.</summary>
/// <param name="Palette">One of <see cref="ChartDefinitionValidator.Palettes"/>.</param>
/// <param name="Stack">Bars, lines and areas with several series are piled on top of each other instead of side by side.</param>
/// <param name="Percent">
/// Stacked, and each category scaled to 100%, so what is shown is each series' share. Implies <paramref name="Stack"/>.
/// </param>
/// <param name="Title">A title drawn at the top of the chart.</param>
/// <param name="XAxisTitle">Names the horizontal axis (the vertical one for a horizontal bar chart is the category axis).</param>
/// <param name="YAxisTitle">Names the vertical axis.</param>
/// <param name="Grid">Grid lines across the plot.</param>
/// <param name="YMin">Where the value axis starts. Null lets the chart decide.</param>
/// <param name="YMax">Where the value axis ends. Null lets the chart decide.</param>
/// <param name="LabelRotate">Turns the category labels, -90 to 90 degrees.</param>
/// <param name="Colors">Your own colours (#rgb or #rrggbb), used instead of the palette, in order.</param>
/// <param name="SeriesColors">A colour for particular series (or slices) by name; the others take the palette or colours.</param>
/// <param name="NumberFormat">How numbers are written. Null leaves them as they are.</param>
/// <param name="LegendPosition">top, bottom, left or right.</param>
/// <param name="Smooth">Lines are curved.</param>
/// <param name="LineWidth">Thickness of lines, 1 to 8.</param>
/// <param name="Symbols">Marks on the points of a line.</param>
/// <param name="BarWidth">Width of a bar as a percentage of its slot, 10 to 100. Null lets the chart decide.</param>
/// <param name="BarRadius">Rounds the ends of bars, 0 to 20.</param>
/// <param name="AreaOpacity">How solid an area is, 0 to 100.</param>
/// <param name="InnerRadius">Size of the hole of a donut, as a percentage of the radius (0 to 90). Null uses the type's own.</param>
/// <param name="Rose">Pie slices differ in radius as well as angle.</param>
/// <param name="PieLabel">What a slice's label says: name, value, percent, name-value or name-percent.</param>
/// <param name="BarBorderStyle">solid, dashed or dotted.</param>
/// <param name="BarBorderWidth">0 (none) to 10.</param>
/// <param name="BarBorderColor">#rgb or #rrggbb. Null is the colour of the bar.</param>
/// <param name="BarGap">Space between the bars of a group, as a percentage of a bar, 0 to 100. Null lets the chart decide.</param>
/// <param name="BarCategoryGap">Space between groups of bars, as a percentage of a bar, 0 to 100. Null lets the chart decide.</param>
/// <param name="LabelPosition">auto, top, bottom, left, right, inside, insideTop or insideBottom.</param>
/// <param name="LabelFont">The value labels.</param>
/// <param name="LegendType">auto, scroll (pages when it is long) or plain.</param>
/// <param name="LegendSelector">Buttons that select all the series or invert the selection.</param>
/// <param name="LegendHeight">Height of the legend in pixels (20 to 1000). Null lets the chart decide.</param>
/// <param name="LegendFont">The legend's text.</param>
/// <param name="XAxis">The horizontal axis.</param>
/// <param name="YAxis">The vertical axis.</param>
/// <param name="XSplitLine">The lines that belong to the horizontal axis (they run up and down).</param>
/// <param name="YSplitLine">The lines that belong to the vertical axis (they run across).</param>
/// <param name="Margin">The space around the plot.</param>
public sealed record ChartStyle(
    bool Legend = true,
    bool Labels = false,
    string Palette = "default",
    bool Stack = false,
    bool Percent = false,
    string? Title = null,
    string? XAxisTitle = null,
    string? YAxisTitle = null,
    bool Grid = true,
    double? YMin = null,
    double? YMax = null,
    int LabelRotate = 0,
    IReadOnlyList<string>? Colors = null,
    IReadOnlyDictionary<string, string>? SeriesColors = null,
    NumberFormat? NumberFormat = null,
    string LegendPosition = "bottom",
    bool Smooth = false,
    int LineWidth = 2,
    bool Symbols = true,
    int? BarWidth = null,
    int BarRadius = 0,
    int AreaOpacity = 70,
    int? InnerRadius = null,
    bool Rose = false,
    string PieLabel = "name",
    string BarBorderStyle = "solid",
    int BarBorderWidth = 0,
    string? BarBorderColor = null,
    int? BarGap = null,
    int? BarCategoryGap = null,
    string LabelPosition = "auto",
    FontStyle? LabelFont = null,
    string LegendType = "auto",
    bool LegendSelector = false,
    int? LegendHeight = null,
    FontStyle? LegendFont = null,
    AxisStyle? XAxis = null,
    AxisStyle? YAxis = null,
    SplitLineStyle? XSplitLine = null,
    SplitLineStyle? YSplitLine = null,
    MarginStyle? Margin = null);

/// <summary>The part of a saved chart that is stored as one JSON document: its query (row limit and sorting included) and style.</summary>
public sealed record ChartDefinition(ChartQuery Query, ChartStyle? Style = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ChartDefinition FromJson(string json) =>
        JsonSerializer.Deserialize<ChartDefinition>(json, Options) ?? new ChartDefinition(new ChartQuery(), new ChartStyle());
}

/// <param name="FolderId">Where a new chart is filed (create only; moving a chart is a separate call). Null is the top level.</param>
public sealed record SaveChartRequest(string Name, string? Description, Guid DatasetId, ChartType Type, ChartDefinition? Definition, Guid? FolderId = null);

public sealed record ChartDto(
    Guid Id,
    string Name,
    string? Description,
    Guid DatasetId,
    ChartType Type,
    ChartDefinition Definition,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    Guid? FolderId = null);
