using DataBoard.Application.Common;
using MySqlConnector;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// Users supply their own connection string, which MySqlConnector would happily use to read local files (certificates,
/// key files), let the server ask for a file from this machine (LOAD DATA LOCAL), use a socket or a pipe, or change how
/// the session behaves. Only a short list of plain options is accepted, and safety settings are then forced on top.
/// </summary>
internal static class MySqlConnectionPolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Server", "Port", "Database", "User ID", "Password", "SSL Mode",
    };

    private static readonly HashSet<string> SystemDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "mysql", "sys", "performance_schema", "information_schema",
    };

    public static MySqlConnectionStringBuilder Parse(string connectionString)
    {
        MySqlConnectionStringBuilder builder;
        try
        {
            builder = new MySqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw new ValidationException("ConnectionString", "The connection string is not valid.");
        }

        var forbidden = builder.Keys.Cast<string>().Where(k => !Allowed.Contains(k)).ToList();
        if (forbidden.Count > 0)
        {
            throw new ValidationException("ConnectionString",
                $"These connection string options are not allowed: {string.Join(", ", forbidden)}. " +
                $"Allowed: Host (or Server), Port, Database, Username (or User ID), Password, SSL Mode.");
        }

        var host = builder.Server?.Trim();
        if (string.IsNullOrEmpty(host))
            throw new ValidationException("ConnectionString", "Host is required.");
        if (host.Contains(',') || host.Contains(' ') || host.StartsWith('/') || host.StartsWith('@') || host.Contains('\\'))
            throw new ValidationException("ConnectionString", "Host must be a single host name or IP address.");
        if (string.IsNullOrWhiteSpace(builder.Database))
            throw new ValidationException("ConnectionString", "Database is required.");
        if (SystemDatabases.Contains(builder.Database.Trim()))
            throw new ValidationException("ConnectionString", "Connect to your own database, not to one of the server's own (mysql, sys, performance_schema).");
        if (string.IsNullOrWhiteSpace(builder.UserID))
            throw new ValidationException("ConnectionString", "Username is required.");
        if (builder.Port is < 1 or > 65535)
            throw new ValidationException("ConnectionString", "Port must be between 1 and 65535.");

        builder.Server = host;
        return builder;
    }

    /// <summary>Forces limits and safe behaviour regardless of what the user supplied.</summary>
    public static string Harden(MySqlConnectionStringBuilder source)
    {
        var builder = new MySqlConnectionStringBuilder(source.ConnectionString)
        {
            Pooling = false,                  // no session state shared between users
            ConnectionTimeout = 5,            // seconds to connect
            DefaultCommandTimeout = 15,       // seconds per command
            AllowLoadLocalInfile = false,     // the server may never ask for a file from this machine
            AllowUserVariables = false,       // "@name" is always a bound parameter, never a session variable
            AllowPublicKeyRetrieval = false,
            ConvertZeroDateTime = true,       // 0000-00-00 is a value some old tables hold; it must not fail the query
            ApplicationName = "DataBoard",
            CharacterSet = "utf8mb4",
        };
        return builder.ConnectionString;
    }
}
