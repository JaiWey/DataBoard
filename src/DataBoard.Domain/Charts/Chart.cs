using DataBoard.Domain.Common;

namespace DataBoard.Domain.Charts;

public enum ChartType
{
    Table,
    Bar,
    Line,
    Pie,
    Area,
    BarHorizontal,
    Donut,
    Number,
    /// <summary>Two dimensions as a grid (X and Y), one measure as the colour of each cell.</summary>
    Heatmap,
    /// <summary>
    /// Points: each row of the result is a point. One dimension names the point (a second splits points into coloured
    /// series), two measures give X and Y, and an optional third gives the size (a bubble chart).
    /// </summary>
    Scatter,
    /// <summary>One dimension is the word and one measure is its weight: heavier words are drawn bigger.</summary>
    WordCloud,
}

/// <summary>
/// A saved chart: a dataset, how to draw it, and the query that feeds it. Dashboards reference charts, so editing one
/// updates every dashboard that shows it. The query and style are one JSON document whose shape is owned and
/// validated by the application layer (it refers to chart queries).
/// </summary>
public sealed class Chart : BaseEntity
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1000;

    private Chart() { }

    public Guid OrganizationId { get; private set; }
    public Guid DatasetId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public ChartType Type { get; private set; }
    public string DefinitionJson { get; private set; } = "{}";
    /// <summary>The folder it is filed in, or null for the top level.</summary>
    public Guid? FolderId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public static Chart Create(
        Guid organizationId,
        Guid datasetId,
        string name,
        string? description,
        ChartType type,
        string definitionJson,
        Guid createdByUserId,
        DateTime nowUtc,
        Guid? folderId = null)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(createdByUserId));

        var chart = new Chart { OrganizationId = organizationId, CreatedByUserId = createdByUserId, CreatedUtc = nowUtc, FolderId = folderId };
        chart.Update(datasetId, name, description, type, definitionJson, nowUtc);
        return chart;
    }

    /// <summary>Filing a chart is organization, not a change to the chart, so it does not count as an update.</summary>
    public void MoveTo(Guid? folderId) => FolderId = folderId;

    public void Update(Guid datasetId, string name, string? description, ChartType type, string definitionJson, DateTime nowUtc)
    {
        if (datasetId == Guid.Empty)
            throw new ArgumentException("Dataset is required.", nameof(datasetId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (name.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Name must be at most {MaxNameLength} characters.", nameof(name));
        if (description is { Length: > MaxDescriptionLength })
            throw new ArgumentException($"Description must be at most {MaxDescriptionLength} characters.", nameof(description));
        if (!Enum.IsDefined(type))
            throw new ArgumentException("Chart type is not valid.", nameof(type));
        if (string.IsNullOrWhiteSpace(definitionJson))
            throw new ArgumentException("Definition is required.", nameof(definitionJson));

        DatasetId = datasetId;
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Type = type;
        DefinitionJson = definitionJson;
        UpdatedUtc = nowUtc;
    }
}
