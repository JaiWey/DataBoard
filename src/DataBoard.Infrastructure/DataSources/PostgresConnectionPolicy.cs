using DataBoard.Application.Common;
using Npgsql;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// Users supply their own connection string, which Npgsql would happily use to read local files
/// (certificates, password files), connect to several hosts, or change session behaviour. Only a short
/// list of plain options is accepted, and safety settings are then forced on top.
/// </summary>
internal static class PostgresConnectionPolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Port", "Database", "Username", "Password", "SSL Mode", "Trust Server Certificate",
    };

    public static NpgsqlConnectionStringBuilder Parse(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
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
                $"Allowed: {string.Join(", ", Allowed.Order())}.");
        }

        var host = builder.Host?.Trim();
        if (string.IsNullOrEmpty(host))
            throw new ValidationException("ConnectionString", "Host is required.");
        if (host.Contains(',') || host.Contains(' ') || host.StartsWith('/') || host.StartsWith('@'))
            throw new ValidationException("ConnectionString", "Host must be a single host name or IP address.");
        if (string.IsNullOrWhiteSpace(builder.Database))
            throw new ValidationException("ConnectionString", "Database is required.");
        if (string.IsNullOrWhiteSpace(builder.Username))
            throw new ValidationException("ConnectionString", "Username is required.");
        if (builder.Port is < 1 or > 65535)
            throw new ValidationException("ConnectionString", "Port must be between 1 and 65535.");

        builder.Host = host;
        return builder;
    }

    /// <summary>Forces limits and read-only behaviour regardless of what the user supplied.</summary>
    public static string Harden(NpgsqlConnectionStringBuilder source)
    {
        var builder = new NpgsqlConnectionStringBuilder(source.ConnectionString)
        {
            Pooling = false,                // no session state shared between users
            Multiplexing = false,
            Timeout = 5,                    // seconds to connect
            CommandTimeout = 15,            // seconds per command
            ApplicationName = "DataBoard",
            IncludeErrorDetail = false,
            Options = "-c TimeZone=UTC -c default_transaction_read_only=on -c statement_timeout=10000 -c lock_timeout=2000 -c idle_in_transaction_session_timeout=15000",
        };
        return builder.ConnectionString;
    }
}
