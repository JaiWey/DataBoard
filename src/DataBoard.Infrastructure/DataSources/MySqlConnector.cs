using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Authentication;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Domain.DataSources;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace DataBoard.Infrastructure.DataSources;

internal sealed class MySqlDataSourceConnector(NetworkGuard networkGuard, ILogger<MySqlDataSourceConnector> logger) : IDataSourceConnector
{
    private const int MaxSchemaColumns = 20_000;
    private const int MaxCellLength = 1_000;
    private const int StatementTimeoutMilliseconds = 10_000;

    public DataSourceType Type => DataSourceType.MySql;

    public void ValidateConnectionString(string connectionString) => MySqlConnectionPolicy.Parse(connectionString);

    public async Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = await OpenAsync(connectionString, ct);
            return new ConnectionTestResult(true, "Connection successful.", connection.ServerVersion, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            logger.LogWarning(ex, "Testing a MySQL connection failed.");
            return new ConnectionTestResult(false, Describe(ex), null, stopwatch.ElapsedMilliseconds);
        }
    }

    public async Task<DatabaseSchema> GetSchemaAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var connection = await OpenAsync(connectionString, ct);
            await using var command = new MySqlCommand(SchemaSql, connection);
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
                    table = (reader.GetString(2) == "BASE TABLE" ? "Table" : "View", []);
                    tables[key] = table;
                    order.Add(key);
                }
                var dataType = reader.GetString(4) == "tinyint" && reader.GetString(5).StartsWith("tinyint(1)", StringComparison.OrdinalIgnoreCase)
                    ? "boolean"
                    : MySqlTypes.Canonical(reader.GetString(4));
                table.Columns.Add(new ColumnSchema(reader.GetString(3), dataType, reader.GetString(6) == "YES"));
            }

            var schemas = order
                .GroupBy(k => k.Schema)
                .Select(g => new SchemaNode(g.Key, g.Select(k => new TableSchema(k.Table, tables[k].Kind, tables[k].Columns)).ToList()))
                .ToList();
            return new DatabaseSchema(schemas, truncated);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct) || ex is MySqlException)
        {
            logger.LogWarning(ex, "Reading the MySQL schema failed.");
            throw new ValidationException("DataSource", Describe(ex));
        }
    }

    public Task<IReadOnlyList<PreviewColumn>> DescribeAsync(
        string connectionString, string validatedSql, IReadOnlyList<object?> parameters, CancellationToken ct = default) =>
        RunReadOnlyAsync<IReadOnlyList<PreviewColumn>>(connectionString, async (connection, transaction) =>
        {
            // LIMIT 0 returns the column metadata without reading any rows.
            await using var command = new MySqlCommand(Wrap(validatedSql, 0), connection, transaction);
            Bind(command, parameters);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return ReadColumns(reader);
        }, ct);

    public Task<QueryPreview> PreviewAsync(
        string connectionString, string validatedSql, int maxRows, IReadOnlyList<object?> parameters, CancellationToken ct = default) =>
        RunReadOnlyAsync(connectionString, async (connection, transaction) =>
        {
            await using var command = new MySqlCommand(Wrap(validatedSql, maxRows + 1), connection, transaction);
            Bind(command, parameters);
            return await ReadResultAsync(command, maxRows, ct);
        }, ct);

    public Task<QueryPreview> ExecuteAsync(string connectionString, ParameterizedQuery query, int maxRows, CancellationToken ct = default) =>
        RunReadOnlyAsync(connectionString, async (connection, transaction) =>
        {
            await using var command = new MySqlCommand(query.Sql, connection, transaction);
            Bind(command, query.Parameters);
            return await ReadResultAsync(command, maxRows, ct);
        }, ct);

    /// <summary>Values travel as driver parameters named databoard_p0, databoard_p1, ...; they are never written into the SQL text.</summary>
    private static void Bind(MySqlCommand command, IReadOnlyList<object?> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
            command.Parameters.AddWithValue(ParameterizedQuery.Name(i), parameters[i] ?? DBNull.Value);
    }

    /// <summary>Reads at most maxRows + 1 rows; the extra one only signals that the result was cut.</summary>
    private static async Task<QueryPreview> ReadResultAsync(MySqlCommand command, int maxRows, CancellationToken ct)
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

    private static List<PreviewColumn> ReadColumns(MySqlDataReader reader) =>
        Enumerable.Range(0, reader.FieldCount)
            .Select(i => new PreviewColumn(reader.GetName(i), MySqlTypes.Canonical(reader.GetDataTypeName(i))))
            .ToList();

    /// <summary>
    /// Every user-written query goes through here: guarded connection, then a read-only transaction with a time limit, then
    /// plain-language errors. The database account should still only be able to read (see the README).
    /// </summary>
    private async Task<T> RunReadOnlyAsync<T>(
        string connectionString, Func<MySqlConnection, MySqlTransaction, Task<T>> work, CancellationToken ct)
    {
        MySqlConnection connection;
        try
        {
            connection = await OpenAsync(connectionString, ct);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            logger.LogWarning(ex, "Opening a MySQL data source failed.");
            throw new ValidationException("DataSource", Describe(ex));
        }

        await using (connection)
        {
            try
            {
                await using var transaction = await connection.BeginTransactionAsync(ct);
                return await work(connection, transaction);
            }
            catch (MySqlException ex) when (!IsConnectionLost(ex))
            {
                // The database's own message (syntax error, timeout, read-only violation) helps the author fix the query.
                throw new ValidationException("Sql", ex.Message);
            }
            catch (Exception ex) when (IsConnectionFailure(ex, ct) || ex is MySqlException)
            {
                logger.LogWarning(ex, "Running a query on a MySQL data source failed.");
                throw new ValidationException("DataSource", Describe(ex));
            }
        }
    }

    /// <summary>Opens a connection that can only read, in UTC, and gives up on a statement after ten seconds.</summary>
    private async Task<MySqlConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var builder = MySqlConnectionPolicy.Parse(connectionString);
        await networkGuard.EnsureAllowedAsync(builder.Server, ct);

        var connection = new MySqlConnection(MySqlConnectionPolicy.Harden(builder));
        try
        {
            await connection.OpenAsync(ct);

            await using var session = connection.CreateCommand();
            session.CommandText =
                "SET SESSION TRANSACTION READ ONLY; " +
                $"SET SESSION max_execution_time = {StatementTimeoutMilliseconds}; " +
                "SET SESSION lock_wait_timeout = 2; " +
                "SET SESSION time_zone = '+00:00'";
            await session.ExecuteNonQueryAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static bool IsConnectionFailure(Exception ex, CancellationToken ct) =>
        ex is MySqlException or TimeoutException or SocketException or IOException or AuthenticationException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    private static bool IsConnectionLost(MySqlException ex) =>
        ex.ErrorCode is MySqlErrorCode.UnableToConnectToHost or MySqlErrorCode.ConnectionCountError or MySqlErrorCode.ServerShutdown
            or MySqlErrorCode.AccessDenied or MySqlErrorCode.DatabaseAccessDenied or MySqlErrorCode.UnknownDatabase
            or MySqlErrorCode.HostNotPrivileged or MySqlErrorCode.ClientInteractionTimeout;

    /// <summary>Plain messages only: raw driver errors can include host names, addresses and server details.</summary>
    private static string Describe(Exception ex) => ex switch
    {
        MySqlException { ErrorCode: MySqlErrorCode.AccessDenied or MySqlErrorCode.DatabaseAccessDenied or MySqlErrorCode.HostNotPrivileged } =>
            "Authentication failed. Check the username and password.",
        MySqlException { ErrorCode: MySqlErrorCode.UnknownDatabase } => "The database does not exist.",
        TimeoutException or OperationCanceledException => "The connection timed out.",
        AuthenticationException => "The secure (TLS) connection could not be established.",
        MySqlException { ErrorCode: MySqlErrorCode.UnableToConnectToHost } => "Could not connect to the server.",
        MySqlException { InnerException: SocketException or IOException } => "Could not connect to the server.",
        MySqlException => "The server rejected the request.",
        _ => "Could not connect to the server.",
    };

    private static object? Normalize(object value)
    {
        switch (value)
        {
            case DBNull:
                return null;
            case string s:
                return Truncate(s);
            case bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan:
                return value;
            case float f:
                return float.IsFinite(f) ? f : f.ToString(CultureInfo.InvariantCulture);
            case double d:
                return double.IsFinite(d) ? d : d.ToString(CultureInfo.InvariantCulture);
            case byte[] bytes:
                return "\\x" + Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 64))) + (bytes.Length > 64 ? "…" : "");
            case Guid g:
                return g.ToString();
            default:
                return Truncate(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    private static string Truncate(string s) => s.Length > MaxCellLength ? s[..MaxCellLength] + "…" : s;

    private const string SchemaSql = """
        SELECT c.TABLE_SCHEMA,
               c.TABLE_NAME,
               t.TABLE_TYPE,
               c.COLUMN_NAME,
               c.DATA_TYPE,
               c.COLUMN_TYPE,
               c.IS_NULLABLE
        FROM information_schema.COLUMNS c
        JOIN information_schema.TABLES t
          ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
        WHERE c.TABLE_SCHEMA NOT IN ('mysql', 'information_schema', 'performance_schema', 'sys')
        ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION
        LIMIT @limit
        """;
}

/// <summary>MySQL's type names, as the names the rest of the application uses, so that charts know what a column can be used for.</summary>
internal static class MySqlTypes
{
    public static string Canonical(string typeName)
    {
        // "INT UNSIGNED", "decimal(10,2)", "VARCHAR" -> the first word, in lower case.
        var name = typeName.Split('(', 2)[0].Trim().Split(' ', 2)[0].ToLowerInvariant();

        return name switch
        {
            "bool" or "boolean" => "boolean",
            "tinyint" or "smallint" or "mediumint" or "int" or "integer" or "year" => "integer",
            "bigint" => "bigint",
            "float" => "real",
            "double" or "real" => "double precision",
            "decimal" or "numeric" or "newdecimal" => "numeric",
            "date" or "newdate" => "date",
            "datetime" or "timestamp" => "timestamp without time zone",
            "time" => "time without time zone",
            "char" or "varchar" or "tinytext" or "text" or "mediumtext" or "longtext" or "enum" or "set" or "var_string" or "string" => "text",
            "json" => "json",
            "binary" or "varbinary" or "tinyblob" or "blob" or "mediumblob" or "longblob" => "bytea",
            _ => name,
        };
    }
}
