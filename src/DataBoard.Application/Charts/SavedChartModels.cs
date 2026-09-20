using System.Text.Json;
using System.Text.Json.Serialization;
using DataBoard.Domain.Charts;

namespace DataBoard.Application.Charts;

/// <summary>How a saved chart is drawn. Only the stored choices; the data comes from the query.</summary>
/// <param name="Palette">One of <see cref="ChartDefinitionValidator.Palettes"/>.</param>
/// <param name="Stack">Bars, lines and areas with several series are piled on top of each other instead of side by side.</param>
/// <param name="Percent">
/// Stacked, and each category scaled to 100%, so what is shown is each series' share. Implies <paramref name="Stack"/>.
/// </param>
public sealed record ChartStyle(bool Legend = true, bool Labels = false, string Palette = "default", bool Stack = false, bool Percent = false);

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
