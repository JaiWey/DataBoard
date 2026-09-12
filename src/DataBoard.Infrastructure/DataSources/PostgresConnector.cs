using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Authentication;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Domain.DataSources;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataBoard.Infrastructure.DataSources;

internal sealed class PostgresConnector(NetworkGuard networkGuard, ILogger<PostgresConnector> logger) : IDataSourceConnector
{
    private const int MaxSchemaColumns = 20_000;
    private const int MaxCellLength = 1_000;

    public DataSourceType Type => DataSourceType.PostgreSql;

    public void ValidateConnectionString(string connectionString) => PostgresConnectionPolicy.Parse(connectionString);

    public async Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = await OpenAsync(connectionString, ct);
            return new ConnectionTestResult(true, "Connection successful.", connection.PostgreSqlVersion.ToString(), stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            logger.LogWarning(ex, "Testing a PostgreSQL connection failed.");
            return new ConnectionTestResult(false, Describe(ex), null, stopwatch.ElapsedMilliseconds);
        }
    }

    public async Task<DatabaseSchema> GetSchemaAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var connection = await OpenAsync(connectionString, ct);
            await using var command = new NpgsqlCommand(SchemaSql, connection);
            command.Parameters.AddWithValue("limit", MaxSchemaColumns + 1);

            var tables = new Dictionary<(string Schema, string Table), (string Kind, List<ColumnSchema> Columns)>();
            var order = new List<(string Schema, string Table)>();
            var count = 0;
            var truncated = false;

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (++count > MaxSchemaColumns) { truncated = true; break; }

                var key = (reader.GetString(0), reader.GetString(1));
                if (!tables.TryGetValue(key, out var table))
                {
                    table = (reader.GetString(2) == "VIEW" ? "View" : "Table", []);
                    tables[key] = table;
                    order.Add(key);
                }
                table.Columns.Add(new ColumnSchema(reader.GetString(3), reader.GetString(4), reader.GetString(5) == "YES"));
            }

            var schemas = order
                .GroupBy(k => k.Schema)
                .Select(g => new SchemaNode(
                    g.Key,
                    g.Select(k => new TableSchema(k.Table, tables[k].Kind, tables[k].Columns)).ToList()))
                .ToList();

            return new DatabaseSchema(schemas, truncated);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            logger.LogWarning(ex, "Reading the PostgreSQL schema failed.");
            throw new ValidationException("DataSource", Describe(ex));
        }
    }

    public Task<IReadOnlyList<PreviewColumn>> DescribeAsync(
        string connectionString, string validatedSql, IReadOnlyList<object?> parameters, CancellationToken ct = default) =>
        RunReadOnlyAsync<IReadOnlyList<PreviewColumn>>(connectionString, async (connection, transaction) =>
        {
            // LIMIT 0 returns the column metadata without reading any rows.
            await using var command = new NpgsqlCommand(Wrap(validatedSql, 0), connection, transaction);
            Bind(command, parameters);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return ReadColumns(reader);
        }, ct);

    public Task<QueryPreview> PreviewAsync(
        string connectionString, string validatedSql, int maxRows, IReadOnlyList<object?> parameters, CancellationToken ct = default) =>
        RunReadOnlyAsync(connectionString, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(Wrap(validatedSql, maxRows + 1), connection, transaction);
            Bind(command, parameters);
            return await ReadResultAsync(command, maxRows, ct);
        }, ct);

    public Task<QueryPreview> ExecuteAsync(string connectionString, ParameterizedQuery query, int maxRows, CancellationToken ct = default) =>
        RunReadOnlyAsync(connectionString, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(query.Sql, connection, transaction);
            Bind(command, query.Parameters);
            return await ReadResultAsync(command, maxRows, ct);
        }, ct);

    /// <summary>Values travel as driver parameters named databoard_p0, databoard_p1, ...; they are never written into the SQL text.</summary>
    private static void Bind(NpgsqlCommand command, IReadOnlyList<object?> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
            command.Parameters.AddWithValue(ParameterizedQuery.Name(i), parameters[i] ?? DBNull.Value);
    }

    /// <summary>Reads at most maxRows + 1 rows; the extra one only signals that the result was cut.</summary>
    private static async Task<QueryPreview> ReadResultAsync(NpgsqlCommand command, int maxRows, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct);

        var columns = ReadColumns(reader);
        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
                row[i] = Normalize(reader.GetValue(i));
            rows.Add(row);
        }

        var truncated = rows.Count > maxRows;
        if (truncated) rows.RemoveAt(rows.Count - 1);

        return new QueryPreview(columns, rows, truncated);
    }

    /// <summary>Newlines keep a trailing "-- comment" in the user's SQL from swallowing the closing parenthesis.</summary>
    private static string Wrap(string validatedSql, int limit) =>
        $"SELECT * FROM (\n{validatedSql}\n) AS databoard_query LIMIT {limit}";

    private static List<PreviewColumn> ReadColumns(NpgsqlDataReader reader) =>
        Enumerable.Range(0, reader.FieldCount)
            .Select(i => new PreviewColumn(reader.GetName(i), reader.GetDataTypeName(i)))
            .ToList();

    /// <summary>
    /// Every user-written query goes through here: guarded connection, then a read-only transaction, then
    /// plain-language errors. The connection also defaults to read-only and has a statement timeout (see the policy).
    /// </summary>
    private async Task<T> RunReadOnlyAsync<T>(
        string connectionString, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> work, CancellationToken ct)
    {
        try
        {
            await using var connection = await OpenAsync(connectionString, ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);

            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
                await readOnly.ExecuteNonQueryAsync(ct);

            return await work(connection, transaction);
        }
        catch (PostgresException ex) when (!IsConnectionState(ex.SqlState))
        {
            // The database's own message (syntax error, timeout, read-only violation) helps the author fix the query.
            throw new ValidationException("Sql", ex.MessageText);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            // The user only gets a plain message (Describe); the real cause is kept for whoever runs the server.
            logger.LogWarning(ex, "Running a query on a PostgreSQL data source failed.");
            throw new ValidationException("DataSource", Describe(ex));
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var builder = PostgresConnectionPolicy.Parse(connectionString);
        await networkGuard.EnsureAllowedAsync(builder.Host!, ct);

        var connection = new NpgsqlConnection(PostgresConnectionPolicy.Harden(builder));
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static bool IsConnectionFailure(Exception ex, CancellationToken ct) =>
        ex is NpgsqlException or TimeoutException or SocketException or IOException or AuthenticationException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    private static bool IsConnectionState(string sqlState) =>
        sqlState.StartsWith("28", StringComparison.Ordinal)      // invalid authorization
        || sqlState.StartsWith("3D", StringComparison.Ordinal)   // invalid catalog (database)
        || sqlState.StartsWith("08", StringComparison.Ordinal);  // connection exception

    /// <summary>Plain messages only: raw driver errors can include host names, addresses and server details.</summary>
    private static string Describe(Exception ex) => ex switch
    {
        PostgresException pg when pg.SqlState.StartsWith("28", StringComparison.Ordinal) =>
            "Authentication failed. Check the username and password.",
        PostgresException pg when pg.SqlState.StartsWith("3D", StringComparison.Ordinal) =>
            "The database does not exist.",
        PostgresException => "The server rejected the connection.",
        TimeoutException or OperationCanceledException => "The connection timed out.",
        AuthenticationException => "The secure (TLS) connection could not be established.",
        _ => "Could not connect to the server.",
    };

    private static object? Normalize(object value)
    {
        switch (value)
        {
            case DBNull:
                return null;
            case string s:
                return s.Length > MaxCellLength ? s[..MaxCellLength] + "…" : s;
            case bool or byte or short or int or long or decimal or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan:
                return value;
            case float f:
                return float.IsFinite(f) ? f : f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case double d:
                return double.IsFinite(d) ? d : d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case byte[] bytes:
                return "\\x" + Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 64))) + (bytes.Length > 64 ? "…" : "");
            case Guid g:
                return g.ToString();
            case Array array:
                return Truncate("{" + string.Join(",", array.Cast<object?>().Select(e => e is null or DBNull ? "NULL" : e.ToString())) + "}");
            default:
                return Truncate(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    private static string Truncate(string s) => s.Length > MaxCellLength ? s[..MaxCellLength] + "…" : s;

    private const string SchemaSql = """
        SELECT c.table_schema,
               c.table_name,
               t.table_type,
               c.column_name,
               CASE WHEN c.data_type IN ('USER-DEFINED', 'ARRAY') THEN c.udt_name ELSE c.data_type END,
               c.is_nullable
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema AND t.table_name = c.table_name
        WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema')
          AND c.table_schema NOT LIKE 'pg\_toast%'
          AND c.table_schema NOT LIKE 'pg\_temp%'
        ORDER BY c.table_schema, c.table_name, c.ordinal_position
        LIMIT @limit
        """;
}
