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
    string PieLabel = "name");

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
