using DataBoard.Domain.Common;

namespace DataBoard.Domain.Datasets;

/// <summary>How a column is used in charts: grouped by (dimension) or aggregated (measure).</summary>
public enum ColumnRole
{
    Dimension = 1,
    Measure = 2,
}

/// <param name="DataType">The database's own type name, for example "integer" or "character varying(256)".</param>
public sealed record DatasetColumn(string Name, string DataType, ColumnRole Role);

/// <summary>A saved, validated query on a data source. Charts are built on datasets, never on raw SQL.</summary>
public sealed class Dataset : BaseEntity
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1000;
    public const int MaxColumns = 500;

    private readonly List<DatasetColumn> _columns = [];

    private Dataset() { }

    public Guid OrganizationId { get; private set; }
    public Guid DataSourceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Already checked by the SQL validator before it reaches the domain, and checked again on every run.</summary>
    public string Sql { get; private set; } = string.Empty;

    /// <summary>The columns the query returned when it was saved.</summary>
    public IReadOnlyList<DatasetColumn> Columns => _columns;

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public static Dataset Create(
        Guid organizationId,
        Guid dataSourceId,
        string name,
        string? description,
        string sql,
        IEnumerable<DatasetColumn> columns,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (dataSourceId == Guid.Empty)
            throw new ArgumentException("Data source is required.", nameof(dataSourceId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(createdByUserId));

        var dataset = new Dataset
        {
            OrganizationId = organizationId,
            DataSourceId = dataSourceId,
            CreatedByUserId = createdByUserId,
            CreatedUtc = nowUtc,
        };
        dataset.Apply(name, description, sql, columns, nowUtc);
        return dataset;
    }

    public void Update(string name, string? description, string sql, IEnumerable<DatasetColumn> columns, DateTime nowUtc) =>
        Apply(name, description, sql, columns, nowUtc);

    private void Apply(string name, string? description, string sql, IEnumerable<DatasetColumn> columns, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (name.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Name must be at most {MaxNameLength} characters.", nameof(name));
        if (description is { Length: > MaxDescriptionLength })
            throw new ArgumentException($"Description must be at most {MaxDescriptionLength} characters.", nameof(description));
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("SQL is required.", nameof(sql));

        var list = columns.ToList();
        if (list.Count == 0)
            throw new ArgumentException("The query must return at least one column.", nameof(columns));
        if (list.Count > MaxColumns)
            throw new ArgumentException($"The query returns too many columns (maximum {MaxColumns}).", nameof(columns));
        if (list.Any(c => string.IsNullOrWhiteSpace(c.Name) || !Enum.IsDefined(c.Role)))
            throw new ArgumentException("Every column needs a name and a valid role.", nameof(columns));

        var duplicate = list.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Column names must be unique; '{duplicate.Key}' appears more than once. Give each column an alias.", nameof(columns));

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Sql = sql;
        _columns.Clear();
        _columns.AddRange(list);
        UpdatedUtc = nowUtc;
    }
}
