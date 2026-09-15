using System.Diagnostics;
using System.Text.RegularExpressions;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Domain.DataSources;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// Reads an uploaded CSV, which was imported into a database file of its own. The file is opened read-only, a query that
/// runs too long is stopped, and the limits SQLite has for the size of a value, a query and an expression are tightened,
/// so that a query cannot use up the server's memory or time. A user never names the file: the connection is only the key
/// of a file in the upload store (see <see cref="CsvConnection"/>).
/// </summary>
public sealed partial class SqliteConnector(IUploadStore store) : IDataSourceConnector
{
    private const int MaxCellLength = 1_000;
    private const int QueryTimeoutSeconds = 10;

    // SQLite's own limits, tightened. A value of 1 MB is far more than a CSV cell needs.
    private const int LimitLength = 1_000_000;
    private const int LimitSqlLength = 100_000;
    private const int LimitExpressionDepth = 100;
    private const int LimitCompoundSelect = 20;
    private const int LimitLikePatternLength = 2_000;

    public DataSourceType Type => DataSourceType.Csv;

    /// <summary>An uploaded file is made by uploading it; there is no connection string to give.</summary>
    public void ValidateConnectionString(string connectionString) =>
        throw new ValidationException("ConnectionString", "A CSV data source is made by uploading a file, not by a connection string.");

    public async Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await RunAsync(connectionString, async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT sqlite_version()";
                var version = (string?)await command.ExecuteScalarAsync(ct);
                return new ConnectionTestResult(true, "The file can be read.", version, stopwatch.ElapsedMilliseconds);
            }, ct);
        }
        catch (ValidationException ex)
        {
            return new ConnectionTestResult(false, ex.Errors.SelectMany(e => e.Value).FirstOrDefault() ?? "The file cannot be read.", null, stopwatch.ElapsedMilliseconds);
        }
    }

    public Task<DatabaseSchema> GetSchemaAsync(string connectionString, CancellationToken ct = default) =>
        RunAsync(connectionString, async connection =>
        {
            const string sql = """
                SELECT m.name, m.type, p.name, p.type, p."notnull"
                FROM sqlite_master m
                JOIN pragma_table_info(m.name) p
                WHERE m.type IN ('table', 'view') AND m.name NOT LIKE 'sqlite\_%' ESCAPE '\'
                ORDER BY m.name, p.cid
                """;

            var tables = new List<TableSchema>();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(ct);

            string? current = null;
            string kind = "Table";
            var columns = new List<ColumnSchema>();
            while (await reader.ReadAsync(ct))
            {
                var table = reader.GetString(0);
                if (table != current)
                {
                    if (current is not null) tables.Add(new TableSchema(current, kind, columns));
                    current = table;
                    kind = reader.GetString(1) == "view" ? "View" : "Table";
                    columns = [];
                }
                columns.Add(new ColumnSchema(reader.GetString(2), CanonicalType(reader.GetString(3), null), reader.GetInt32(4) == 0));
            }
            if (current is not null) tables.Add(new TableSchema(current, kind, columns));

            // "main" is what SQLite calls the file's own schema; a table view names its tables with it.
            return new DatabaseSchema(tables.Count == 0 ? [] : [new SchemaNode("main", tables)], Truncated: false);
        }, ct);

    public Task<IReadOnlyList<PreviewColumn>> DescribeAsync(
        string connectionString, string validatedSql, IReadOnlyList<object?> parameters, CancellationToken ct = default) =>
        RunAsync<IReadOnlyList<PreviewColumn>>(connectionString, async connection =>
        {
            // A column that comes straight from a table has the type it was declared with; one that is computed has none, so
            // what a few rows hold decides (a sum is a number, a date function gives text).
            await using var command = Command(connection, Wrap(validatedSql, 100), parameters);
            await using var reader = await command.ExecuteReaderAsync(ct);

            var declared = Enumerable.Range(0, reader.FieldCount).Select(reader.GetDataTypeName).ToArray();
            var observed = new Type?[reader.FieldCount];
            while (await reader.ReadAsync(ct))
            {
                for (var i = 0; i < observed.Length; i++)
                {
                    if (observed[i] is null && !await reader.IsDBNullAsync(i, ct)) observed[i] = reader.GetFieldType(i);
                }
            }

            return Enumerable.Range(0, declared.Length).Select(i => new PreviewColumn(reader.GetName(i), CanonicalType(declared[i], observed[i]))).ToList();
        }, ct);

    public Task<QueryPreview> PreviewAsync(
        string connectionString, string validatedSql, int maxRows, IReadOnlyList<object?> parameters, CancellationToken ct = default) =>
        RunAsync(connectionString, async connection =>
        {
            await using var command = Command(connection, Wrap(validatedSql, maxRows + 1), parameters);
            return await ReadResultAsync(command, maxRows, ct);
        }, ct);

    public Task<QueryPreview> ExecuteAsync(string connectionString, ParameterizedQuery query, int maxRows, CancellationToken ct = default) =>
        RunAsync(connectionString, async connection =>
        {
            await using var command = Command(connection, query.Sql, query.Parameters);
            return await ReadResultAsync(command, maxRows, ct);
        }, ct);

    // ---- running ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Opens the file for reading only and runs the work with a time limit. SQLite has no statement timeout of its own: a
    /// query that never finishes (a join of a table with itself, summed) is stopped by asking SQLite to interrupt it.
    /// </summary>
    private async Task<T> RunAsync<T>(string connectionString, Func<SqliteConnection, Task<T>> work, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(QueryTimeoutSeconds));

        SqliteConnection? connection = null;
        try
        {
            connection = Open(connectionString);
            await using var _ = connection;
            await connection.OpenAsync(limit.Token);

            // Only reading, whatever the SQL says, and no bigger than what a CSV needs.
            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA query_only = ON";
                await pragma.ExecuteNonQueryAsync(limit.Token);
            }
            var handle = connection.Handle!;
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_LENGTH, LimitLength);
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_SQL_LENGTH, LimitSqlLength);
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_EXPR_DEPTH, LimitExpressionDepth);
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_COMPOUND_SELECT, LimitCompoundSelect);
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_LIKE_PATTERN_LENGTH, LimitLikePatternLength);

            // The last lock: SQLite asks before it does anything, and only reading is allowed (no ATTACH of another file, no
            // PRAGMA to undo query_only). The validator has already refused such SQL; this holds even if it ever lets one by.
            raw.sqlite3_set_authorizer(handle, Authorize, null);

            using var stop = limit.Token.Register(() => raw.sqlite3_interrupt(handle));
            return await work(connection);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_INTERRUPT || limit.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new ValidationException("Sql", $"The query took too long (more than {QueryTimeoutSeconds} seconds). Narrow it down.");
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new ValidationException("Sql", $"The query took too long (more than {QueryTimeoutSeconds} seconds). Narrow it down.");
        }
        catch (SqliteException ex)
        {
            throw new ValidationException("Sql", Message(ex));
        }
    }

    private static int Authorize(object? userData, int action, utf8z p1, utf8z p2, utf8z database, utf8z source) =>
        action is raw.SQLITE_SELECT or raw.SQLITE_READ or raw.SQLITE_FUNCTION or raw.SQLITE_RECURSIVE
            // pragma_table_info(...) lists a table's columns; no other pragma is reachable
            || (action == raw.SQLITE_PRAGMA && p1.utf8_to_string() is "table_info" or "table_xinfo")
            ? raw.SQLITE_OK
            : raw.SQLITE_DENY;

    private SqliteConnection Open(string connectionString)
    {
        if (!CsvConnection.TryGetKey(connectionString, out var key))
            throw new ValidationException("ConnectionString", "This data source has no file.");

        string path;
        try
        {
            path = store.PathOf(key);
        }
        catch (ArgumentException)
        {
            throw new ValidationException("ConnectionString", "This data source has no file.");
        }
        if (!File.Exists(path))
            throw new ValidationException("DataSource", "The file of this data source is missing. Upload it again.");

        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false, // no state shared between users
            DefaultTimeout = QueryTimeoutSeconds,
        }.ConnectionString);
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, IReadOnlyList<object?> parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < parameters.Count; i++)
        {
            // A decimal would be stored as text, and text does not compare with numbers the way a number does.
            var value = parameters[i] switch { null => DBNull.Value, decimal d => (object)(double)d, var v => v };
            command.Parameters.AddWithValue(ParameterizedQuery.Name(i), value);
        }
        return command;
    }

    /// <summary>Newlines keep a trailing "-- comment" in the user's SQL from swallowing the closing parenthesis.</summary>
    private static string Wrap(string validatedSql, int limit) =>
        $"SELECT * FROM (\n{validatedSql}\n) AS databoard_query LIMIT {limit}";

    /// <summary>Reads at most maxRows + 1 rows; the extra one only signals that the result was cut.</summary>
    private static async Task<QueryPreview> ReadResultAsync(SqliteCommand command, int maxRows, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct);

        var declared = Enumerable.Range(0, reader.FieldCount).Select(reader.GetDataTypeName).ToArray();
        var columns = new List<PreviewColumn>();
        var isBoolean = new bool[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var canonical = CanonicalType(declared[i], null);
            columns.Add(new PreviewColumn(reader.GetName(i), canonical));
            isBoolean[i] = canonical == "boolean";
        }

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = Normalize(reader.GetValue(i), isBoolean[i]);
            rows.Add(row);
        }

        var truncated = rows.Count > maxRows;
        if (truncated) rows.RemoveAt(rows.Count - 1);
        return new QueryPreview(columns, rows, truncated);
    }

    private static object? Normalize(object value, bool boolean) => value switch
    {
        DBNull => null,
        long l when boolean => l != 0,
        string s => s.Length > MaxCellLength ? s[..MaxCellLength] + "…" : s,
        long or double => value,
        byte[] bytes => "\\x" + Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 64))) + (bytes.Length > 64 ? "…" : ""),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
    };

    // ---- types and messages ----------------------------------------------------------------------------------

    /// <summary>
    /// SQLite's declared type (or, for a computed column, the kind of value seen), as the name the rest of the application
    /// uses, so that charts know what a column can be used for.
    /// </summary>
    internal static string CanonicalType(string? declared, Type? observed)
    {
        var type = (declared ?? string.Empty).ToUpperInvariant();

        if (type.Contains("BOOL")) return "boolean";
        if (type.Contains("DATETIME") || type.Contains("TIMESTAMP")) return "timestamp without time zone";
        if (type.Contains("DATE")) return "date";
        if (type.Contains("INT")) return "bigint";
        if (type.Contains("REAL") || type.Contains("FLOA") || type.Contains("DOUB")) return "double precision";
        if (type.Contains("NUMERIC") || type.Contains("DECIMAL")) return "numeric";
        if (type.Contains("CHAR") || type.Contains("CLOB") || type.Contains("TEXT")) return "text";

        // No declared type (the driver says "BLOB" for that too, and an import makes no blob columns): a computed column.
        // What it holds says what it is.
        if (observed == typeof(long)) return "bigint";
        if (observed == typeof(double)) return "double precision";
        if (observed == typeof(byte[])) return "bytea";
        return "text";
    }

    [GeneratedRegex(@"^SQLite Error \d+: '(.*)'\.?$", RegexOptions.Singleline)]
    private static partial Regex ErrorPattern();

    /// <summary>"SQLite Error 1: 'no such column: x'." as just "no such column: x": what the author needs to fix the query.</summary>
    private static string Message(SqliteException ex)
    {
        var match = ErrorPattern().Match(ex.Message);
        return match.Success ? match.Groups[1].Value : ex.Message;
    }
}
