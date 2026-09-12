using DataBoard.Domain.DataSources;

namespace DataBoard.Application.DataSources;

public sealed record ConnectionTestResult(bool Success, string Message, string? ServerVersion, long LatencyMs);

public sealed record ColumnSchema(string Name, string DataType, bool Nullable);
public sealed record TableSchema(string Name, string Kind, IReadOnlyList<ColumnSchema> Columns);
public sealed record SchemaNode(string Name, IReadOnlyList<TableSchema> Tables);

/// <param name="Truncated">True when the database has more columns than the browser returns.</param>
public sealed record DatabaseSchema(IReadOnlyList<SchemaNode> Schemas, bool Truncated);

public sealed record PreviewColumn(string Name, string DataType);

/// <param name="Truncated">True when the query returned more rows than were requested.</param>
public sealed record QueryPreview(IReadOnlyList<PreviewColumn> Columns, IReadOnlyList<object?[]> Rows, bool Truncated);

/// <summary>
/// SQL with bound values. The text refers to them as <c>@databoard_p0</c>, <c>@databoard_p1</c>, ... (see
/// <see cref="Name"/>) in the order of <paramref name="Parameters"/>. Values are never written into the text.
/// The long prefix keeps the names from colliding with anything inside a user's own SQL.
/// </summary>
public sealed record ParameterizedQuery(string Sql, IReadOnlyList<object?> Parameters)
{
    /// <summary>Parameter name without the leading @, for the given position.</summary>
    public static string Name(int index) => $"databoard_p{index}";
}

/// <summary>Talks to one kind of external database. Implementations must never expose credentials in results or errors.</summary>
public interface IDataSourceConnector
{
    DataSourceType Type { get; }

    /// <summary>Rejects connection strings that use options we do not allow. Throws <see cref="Common.ValidationException"/>.</summary>
    void ValidateConnectionString(string connectionString);

    /// <summary>Connection problems are reported in the result (Success = false), not thrown.</summary>
    Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken ct = default);

    Task<DatabaseSchema> GetSchemaAsync(string connectionString, CancellationToken ct = default);

    /// <summary>Returns the column names and database types a validated query produces, without returning any rows.</summary>
    /// <param name="parameters">Values for the <c>@databoard_pN</c> parameters the SQL refers to, in order (empty if none).</param>
    Task<IReadOnlyList<PreviewColumn>> DescribeAsync(
        string connectionString, string validatedSql, IReadOnlyList<object?> parameters, CancellationToken ct = default);

    /// <summary>
    /// Runs a query produced by a <see cref="Charts.IChartSqlRenderer"/> (read-only, with timeouts). The renderer has
    /// already applied <c>LIMIT maxRows + 1</c>, so one extra row means the result was cut.
    /// </summary>
    Task<QueryPreview> ExecuteAsync(string connectionString, ParameterizedQuery query, int maxRows, CancellationToken ct = default);

    /// <summary>Runs an already validated, single read-only query and returns at most <paramref name="maxRows"/> rows.</summary>
    Task<QueryPreview> PreviewAsync(
        string connectionString, string validatedSql, int maxRows, IReadOnlyList<object?> parameters, CancellationToken ct = default);
}

public interface ISqlQueryValidator
{
    /// <summary>
    /// Accepts exactly one read-only query (SELECT, WITH ... SELECT, set operations) and returns it normalised.
    /// Anything else throws <see cref="Common.ValidationException"/>. This is one layer of defence; the connector
    /// must also run the query in a read-only transaction.
    /// </summary>
    string ValidateReadOnly(DataSourceType type, string sql);
}
