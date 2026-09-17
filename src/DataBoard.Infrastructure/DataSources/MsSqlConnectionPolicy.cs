using System.Data.Common;
using System.Text.RegularExpressions;
using DataBoard.Application.Common;
using Microsoft.Data.SqlClient;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// Users supply their own connection string. For SQL Server that could mean signing in as the server's own Windows identity
/// (Integrated Security, Authentication=...), attaching a database file from this machine, a named pipe or a shared-memory
/// connection to this machine, or a second server to fail over to. Only a short list of plain options is accepted, a server
/// is a host name (or address) with an optional port, and safety settings are then forced on top.
/// </summary>
internal static partial class MsSqlConnectionPolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data Source", "Initial Catalog", "User ID", "Password", "Encrypt", "Trust Server Certificate",
    };

    private static readonly HashSet<string> SystemDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "master", "model", "msdb", "tempdb",
    };

    [GeneratedRegex(@"^(?:tcp:)?(?<host>[A-Za-z0-9][A-Za-z0-9._-]*)(?:,(?<port>\d{1,5}))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ServerPattern();

    /// <summary>A parsed connection string and the host it will connect to (for the network check).</summary>
    public sealed record Parsed(SqlConnectionStringBuilder Builder, string Host);

    public static Parsed Parse(string connectionString)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ValidationException("ConnectionString", "The connection string is not valid.");
        }

        // The builder lists every option it knows. What the person wrote is what it writes back, under the options' own names
        // (so "Server" and "Uid" are seen as "Data Source" and "User ID").
        var written = new DbConnectionStringBuilder { ConnectionString = builder.ConnectionString }.Keys.Cast<string>();
        var forbidden = written.Where(k => !Allowed.Contains(k)).ToList();
        if (forbidden.Count > 0)
        {
            throw new ValidationException("ConnectionString",
                $"These connection string options are not allowed: {string.Join(", ", forbidden)}. " +
                "Allowed: Server (host or host,port), Database, User ID, Password, Encrypt, Trust Server Certificate.");
        }

        var server = ServerPattern().Match(builder.DataSource?.Trim() ?? string.Empty);
        if (!server.Success)
            throw new ValidationException("ConnectionString", "Server must be a host name or IP address, optionally followed by a comma and a port.");
        if (server.Groups["port"].Success && (!int.TryParse(server.Groups["port"].Value, out var port) || port is < 1 or > 65535))
            throw new ValidationException("ConnectionString", "Port must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            throw new ValidationException("ConnectionString", "Database is required.");
        if (SystemDatabases.Contains(builder.InitialCatalog.Trim()))
            throw new ValidationException("ConnectionString", "Connect to your own database, not to one of the server's own (master, model, msdb, tempdb).");
        if (string.IsNullOrWhiteSpace(builder.UserID))
            throw new ValidationException("ConnectionString", "Username is required.");

        builder.DataSource = builder.DataSource!.Trim();
        return new Parsed(builder, server.Groups["host"].Value);
    }

    /// <summary>Forces limits and safe behaviour regardless of what the user supplied.</summary>
    public static string Harden(SqlConnectionStringBuilder source)
    {
        var builder = new SqlConnectionStringBuilder(source.ConnectionString)
        {
            Pooling = false,                  // no session state shared between users
            ConnectTimeout = 5,               // seconds to connect
            CommandTimeout = 15,              // seconds per command
            ConnectRetryCount = 0,            // fail at once rather than retry for a while
            MultipleActiveResultSets = false,
            Enlist = false,
            PersistSecurityInfo = false,
            ApplicationName = "DataBoard",
        };
        return builder.ConnectionString;
    }
}
