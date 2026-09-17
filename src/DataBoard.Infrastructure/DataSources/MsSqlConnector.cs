using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Authentication;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Domain.DataSources;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// SQL Server has no read-only transaction, so a query here is held to reading by what comes before it: the validator (one
/// SELECT, nothing that reaches outside the data), a transaction that is never committed, a lock timeout and a time limit,
/// and, above all, a database account that can only read (see the README).
/// </summary>
internal sealed class MsSqlDataSourceConnector(NetworkGuard networkGuard, ILogger<MsSqlDataSourceConnector> logger) : IDataSourceConnector
{
    private const int MaxSchemaColumns = 20_000;
    private const int MaxCellLength = 1_000;
    private const int StatementTimeoutSeconds = 10;
    private const int TimeoutErrorNumber = -2;

    public DataSourceType Type => DataSourceType.SqlServer;

    public void ValidateConnectionString(string connectionString) => MsSqlConnectionPolicy.Parse(connectionString);

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
            logger.LogWarning(ex, "Testing a SQL Server connection failed.");
            return new ConnectionTestResult(false, Describe(ex), null, stopwatch.ElapsedMilliseconds);
        }
    }

    public async Task<DatabaseSchema> GetSchemaAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var connection = await OpenAsync(connectionString, ct);
            await using var command = new SqlCommand(SchemaSql, connection) { CommandTimeout = StatementTimeoutSeconds };
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
                table.Columns.Add(new ColumnSchema(reader.GetString(3), MsSqlTypes.Canonical(reader.GetString(4)), reader.GetString(5) == "YES"));
            }

            var schemas = order
                .GroupBy(k => k.Schema)
                .Select(g => new SchemaNode(g.Key, g.Select(k => new TableSchema(k.Table, tables[k].Kind, tables[k].Columns)).ToList()))
                .ToList();
            return new DatabaseSchema(schemas, truncated);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct) || ex is SqlException)
        {
            logger.LogWarning(ex, "Reading the SQL Server schema failed.");
            throw new ValidationException("DataSource", Describe(ex));
        }
    }

    public Task<IReadOnlyList<PreviewColumn>> DescribeAsync(
        string connectionString, string validatedSql, IReadOnlyList<object?> parameters, CancellationToken ct = default) =>
        RunReadOnlyAsync<IReadOnlyList<PreviewColumn>>(connectionString, async (connection, transaction) =>
        {
            // TOP (0) returns the column metadata without reading any rows.
            await using var command = Command(MsSqlDataset.Wrap(validatedSql, 0), connection, transaction, parameters);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return ReadColumns(reader);
        }, ct);

    public Task<QueryPreview> PreviewAsync(
        string connectionString, string validatedSql, int maxRows, IReadOnlyList<object?> parameters, CancellationToken ct = default) =>
        RunReadOnlyAsync(connectionString, async (connection, transaction) =>
        {
            await using var command = Command(MsSqlDataset.Wrap(validatedSql, maxRows + 1), connection, transaction, parameters);
            return await ReadResultAsync(command, maxRows, ct);
        }, ct);

    public Task<QueryPreview> ExecuteAsync(string connectionString, ParameterizedQuery query, int maxRows, CancellationToken ct = default) =>
        RunReadOnlyAsync(connectionString, async (connection, transaction) =>
        {
            await using var command = Command(query.Sql, connection, transaction, query.Parameters);
            return await ReadResultAsync(command, maxRows, ct);
        }, ct);

    /// <summary>Values travel as driver parameters named databoard_p0, databoard_p1, ...; they are never written into the SQL text.</summary>
    private static SqlCommand Command(string sql, SqlConnection connection, SqlTransaction transaction, IReadOnlyList<object?> parameters)
    {
        var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = StatementTimeoutSeconds };
        for (var i = 0; i < parameters.Count; i++)
            command.Parameters.AddWithValue(ParameterizedQuery.Name(i), parameters[i] ?? DBNull.Value);
        return command;
    }

    /// <summary>Reads at most maxRows + 1 rows; the extra one only signals that the result was cut.</summary>
    private static async Task<QueryPreview> ReadResultAsync(SqlCommand command, int maxRows, CancellationToken ct)
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

    private static List<PreviewColumn> ReadColumns(SqlDataReader reader) =>
        Enumerable.Range(0, reader.FieldCount)
            .Select(i => new PreviewColumn(reader.GetName(i), MsSqlTypes.Canonical(reader.GetDataTypeName(i))))
            .ToList();

    /// <summary>
    /// Every user-written query goes through here: guarded connection, then a transaction that is never committed, then
    /// plain-language errors.
    /// </summary>
    private async Task<T> RunReadOnlyAsync<T>(
        string connectionString, Func<SqlConnection, SqlTransaction, Task<T>> work, CancellationToken ct)
    {
        SqlConnection connection;
        try
        {
            connection = await OpenAsync(connectionString, ct);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            logger.LogWarning(ex, "Opening a SQL Server data source failed.");
            throw new ValidationException("DataSource", Describe(ex));
        }

        await using (connection)
        {
            try
            {
                // Disposed without a commit, so the transaction is rolled back whatever the SQL did.
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
                return await work(connection, transaction);
            }
            catch (SqlException ex) when (ex.Number == TimeoutErrorNumber)
            {
                throw new ValidationException("Sql", $"The query took too long (more than {StatementTimeoutSeconds} seconds). Narrow it down.");
            }
            catch (SqlException ex) when (!IsConnectionLost(ex))
            {
                // The database's own message (syntax error, missing permission) helps the author fix the query.
                throw new ValidationException("Sql", ex.Message);
            }
            catch (Exception ex) when (IsConnectionFailure(ex, ct))
            {
                logger.LogWarning(ex, "Running a query on a SQL Server data source failed.");
                throw new ValidationException("DataSource", Describe(ex));
            }
        }
    }

    /// <summary>Opens a connection with a lock timeout, and without row counts in the way.</summary>
    private async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var parsed = MsSqlConnectionPolicy.Parse(connectionString);
        await networkGuard.EnsureAllowedAsync(parsed.Host, ct);

        var connection = new SqlConnection(MsSqlConnectionPolicy.Harden(parsed.Builder));
        try
        {
            await connection.OpenAsync(ct);

            await using var session = connection.CreateCommand();
            session.CommandText = "SET NOCOUNT ON; SET LOCK_TIMEOUT 2000;";
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
        ex is SqlException or TimeoutException or SocketException or IOException or AuthenticationException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    // 18456 login failed, 4060/4063 cannot open the database, 233/10054/10060/-1/53 the connection itself.
    private static bool IsConnectionLost(SqlException ex) => ex.Number is 18456 or 4060 or 4063 or 233 or 10054 or 10060 or -1 or 53 or 40 or 64;

    /// <summary>Plain messages only: raw driver errors can include host names, addresses and server details.</summary>
    private static string Describe(Exception ex) => ex switch
    {
        SqlException { Number: 18456 } => "Authentication failed. Check the username and password.",
        SqlException { Number: 4060 or 4063 } => "The database does not exist, or the account may not open it.",
        SqlException { Number: TimeoutErrorNumber } or TimeoutException or OperationCanceledException => "The connection timed out.",
        AuthenticationException => "The secure (TLS) connection could not be established.",
        SqlException s when s.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) || s.Message.Contains("SSL", StringComparison.Ordinal) =>
            "The secure (TLS) connection could not be established.",
        SqlException { Number: 53 or 40 or 10060 or 10061 or -1 } => "Could not connect to the server.",
        SqlException => "The server rejected the request.",
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
            case bool or byte or short or int or long or decimal or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan:
                return value;
            case float f:
                return float.IsFinite(f) ? f : f.ToString(CultureInfo.InvariantCulture);
            case double d:
                return double.IsFinite(d) ? d : d.ToString(CultureInfo.InvariantCulture);
            case byte[] bytes:
                return "0x" + Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 64))) + (bytes.Length > 64 ? "…" : "");
            case Guid g:
                return g.ToString();
            default:
                return Truncate(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    private static string Truncate(string s) => s.Length > MaxCellLength ? s[..MaxCellLength] + "…" : s;

    // INFORMATION_SCHEMA only lists what the account may see.
    private const string SchemaSql = """
        SELECT TOP (@limit)
               c.TABLE_SCHEMA,
               c.TABLE_NAME,
               t.TABLE_TYPE,
               c.COLUMN_NAME,
               c.DATA_TYPE,
               c.IS_NULLABLE
        FROM INFORMATION_SCHEMA.COLUMNS c
        JOIN INFORMATION_SCHEMA.TABLES t
          ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
        WHERE c.TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
        ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION
        """;
}

/// <summary>SQL Server's type names, as the names the rest of the application uses, so that charts know what a column can be used for.</summary>
internal static class MsSqlTypes
{
    public static string Canonical(string typeName)
    {
        var name = typeName.Split('(', 2)[0].Trim().ToLowerInvariant();

        return name switch
        {
            "bit" => "boolean",
            "tinyint" or "smallint" => "smallint",
            "int" => "integer",
            "bigint" => "bigint",
            "real" => "real",
            "float" => "double precision",
            "decimal" or "numeric" or "money" or "smallmoney" => "numeric",
            "date" => "date",
            "datetime" or "datetime2" or "smalldatetime" => "timestamp without time zone",
            "datetimeoffset" => "timestamp with time zone",
            "time" => "time without time zone",
            "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "sysname" => "text",
            "uniqueidentifier" => "uuid",
            "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => "bytea",
            _ => name,
        };
    }
}
