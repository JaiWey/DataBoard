using DataBoard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Runs the real API against a throwaway PostgreSQL container (needs Docker).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ReaderPassword = "reader-pw-123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public const string MySqlReaderPassword = "reader-pw-123";

    /// <summary>Testcontainers gives the root account the same password as the default user.</summary>
    public const string MySqlRootPassword = "mysql";

    // The image is the one already on a developer machine; MySQL 8 is also what most hosted MySQL is.
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8").Build();

    public const string MsSqlReaderPassword = "Reader-pw-123!";

    private readonly MsSqlContainer _mssql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _mysql.StartAsync(), _mssql.StartAsync());

        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        await SeedTargetDatabaseAsync();
        await SeedMySqlAsync();
        await SeedMsSqlAsync();
    }

    /// <summary>
    /// The SQL Server container is the "customer database" for SQL Server data sources: database <c>shop</c> with a small fixed
    /// table, and a <c>reader</c> login that can only SELECT it (not the other table).
    /// </summary>
    private async Task SeedMsSqlAsync()
    {
        async Task RunAsync(string database, string sql)
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_mssql.GetConnectionString()) { InitialCatalog = database };
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new Microsoft.Data.SqlClient.SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        await RunAsync("master", $"CREATE DATABASE shop; CREATE LOGIN reader WITH PASSWORD = '{MsSqlReaderPassword}', CHECK_POLICY = OFF;");
        await RunAsync("shop", $$"""
            CREATE TABLE dbo.orders (
                id INT IDENTITY PRIMARY KEY, region NVARCHAR(50) NOT NULL, product NVARCHAR(50) NULL, quantity INT NULL, price DECIMAL(10,2) NOT NULL,
                ordered_on DATE NOT NULL, created_at DATETIME2 NOT NULL, paid BIT NOT NULL, notes NVARCHAR(MAX) NULL);
            INSERT INTO dbo.orders (region, product, quantity, price, ordered_on, created_at, paid, notes) VALUES
              ('Auckland',     'Phone',  2, 500.00, '2026-01-15', '2026-01-15T10:00:00', 1, 'rush order'),
              ('Auckland',     'Laptop', 1, 1500.00,'2026-01-20', '2026-01-20T11:00:00', 0, NULL),
              ('Wellington',   'Phone',  3, 500.00, '2026-02-03', '2026-02-03T09:30:00', 1, '50%_off\ sale'),
              ('Wellington',   'Phone',  1, 450.00, '2026-02-17', '2026-02-17T23:30:00', 1, NULL),
              ('Christchurch', 'Tablet', 5, 300.00, '2026-03-05', '2026-03-05T00:00:00', 0, NULL),
              ('Christchurch', 'Phone',  4, 520.00, '2026-03-10', '2026-03-10T12:00:00', 1, 'rush order'),
              ('Auckland',     'Phone',  1, 480.00, '2026-03-28', '2026-03-28T08:00:00', 1, '[tag] x'),
              ('Dunedin',      NULL,  NULL, 100.00, '2026-03-31', '2026-03-31T23:59:59', 1, NULL);
            CREATE TABLE dbo.secret_table (id INT, token NVARCHAR(50));
            INSERT INTO dbo.secret_table VALUES (1, 'do-not-show');
            CREATE USER reader FOR LOGIN reader;
            GRANT SELECT ON dbo.orders TO reader;
            """);
    }

    /// <summary>Connection string for the seeded SQL Server database as the limited <c>reader</c> login (or <c>sa</c>).</summary>
    public string MsSqlConnectionString(string username = "reader", string? password = MsSqlReaderPassword, string? database = "shop", int? port = null)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_mssql.GetConnectionString())
        {
            UserID = username,
            Password = password ?? string.Empty,
            InitialCatalog = database ?? string.Empty,
            TrustServerCertificate = true,
        };
        if (port is not null) builder.DataSource = builder.DataSource.Split(',')[0] + "," + port;
        return builder.ConnectionString;
    }

    /// <summary>The <c>sa</c> account's password, which Testcontainers chooses.</summary>
    public string MsSqlAdminPassword => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_mssql.GetConnectionString()).Password;

    /// <summary>
    /// The MySQL container is the "customer database" for MySQL data sources: a small fixed table to compute expected
    /// answers from, and a <c>reader</c> account that can only SELECT it (and not the other table).
    /// </summary>
    private async Task SeedMySqlAsync()
    {
        await using var connection = new MySqlConnection(MySqlConnectionString("root", MySqlRootPassword));
        await connection.OpenAsync();
        await using var command = new MySqlCommand($$"""
            CREATE TABLE orders (
                id INT AUTO_INCREMENT PRIMARY KEY, region VARCHAR(50) NOT NULL, product VARCHAR(50), quantity INT, price DECIMAL(10,2) NOT NULL,
                ordered_on DATE NOT NULL, created_at DATETIME NOT NULL, paid TINYINT(1) NOT NULL, notes TEXT, data JSON);
            INSERT INTO orders (region, product, quantity, price, ordered_on, created_at, paid, notes, data) VALUES
              ('Auckland',     'Phone',  2, 500.00, '2026-01-15', '2026-01-15 10:00:00', 1, 'rush order',    '{"k":1}'),
              ('Auckland',     'Laptop', 1, 1500.00,'2026-01-20', '2026-01-20 11:00:00', 0, NULL,            '{"a":1}'),
              ('Wellington',   'Phone',  3, 500.00, '2026-02-03', '2026-02-03 09:30:00', 1, '50%_off\\ sale', NULL),
              ('Wellington',   'Phone',  1, 450.00, '2026-02-17', '2026-02-17 23:30:00', 1, NULL,            NULL),
              ('Christchurch', 'Tablet', 5, 300.00, '2026-03-05', '2026-03-05 00:00:00', 0, NULL,            NULL),
              ('Christchurch', 'Phone',  4, 520.00, '2026-03-10', '2026-03-10 12:00:00', 1, 'rush order',    NULL),
              ('Auckland',     'Phone',  1, 480.00, '2026-03-28', '2026-03-28 08:00:00', 1, NULL,            NULL),
              ('Dunedin',      NULL,  NULL, 100.00, '2026-03-31', '2026-03-31 23:59:59', 1, NULL,            NULL);
            CREATE TABLE secret_table (id INT, token VARCHAR(50));
            INSERT INTO secret_table VALUES (1, 'do-not-show');
            CREATE USER 'reader'@'%' IDENTIFIED BY '{{MySqlReaderPassword}}';
            GRANT SELECT ON test.orders TO 'reader'@'%';
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Connection string for the seeded MySQL database as the limited <c>reader</c> account (or root).</summary>
    public string MySqlConnectionString(string username = "reader", string? password = MySqlReaderPassword, string? host = null, int? port = null)
    {
        var builder = new MySqlConnectionStringBuilder(_mysql.GetConnectionString()) { UserID = username, Password = password ?? string.Empty };
        if (host is not null) builder.Server = host;
        if (port is not null) builder.Port = (uint)port.Value;
        return builder.ConnectionString;
    }

    /// <summary>Runs SQL on the MySQL container as its root account, behind the API's back.</summary>
    public async Task ExecuteOnMySqlAsync(string sql)
    {
        await using var connection = new MySqlConnection(MySqlConnectionString("root", MySqlRootPassword));
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// The same container doubles as the "customer database" that data sources point at. Data lives in the
    /// public schema; the role <c>reader</c> has SELECT on two relations only, like a real least-privilege account.
    /// </summary>
    private async Task SeedTargetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            CREATE TABLE sales (id serial PRIMARY KEY, region text NOT NULL, amount numeric(10,2) NOT NULL, sold_at timestamptz, note text);
            INSERT INTO sales (region, amount, sold_at) SELECT 'Auckland', g, now() FROM generate_series(1, 150) g;
            CREATE VIEW big_sales AS SELECT * FROM sales WHERE amount > 100;
            CREATE TABLE secret_table (id int, token text);
            INSERT INTO secret_table VALUES (1, 'do-not-show');
            CREATE FUNCTION do_write() RETURNS int LANGUAGE sql VOLATILE AS $$ INSERT INTO sales (region, amount) VALUES ('hacked', 1) RETURNING 1 $$;

            -- Small, fixed data set that chart tests compute expected answers from.
            CREATE TABLE orders (
                id serial PRIMARY KEY, region text NOT NULL, product text, quantity int, price numeric(10,2) NOT NULL,
                ordered_on date NOT NULL, created_at timestamptz NOT NULL, paid boolean NOT NULL, customer uuid, notes text, data jsonb);
            INSERT INTO orders (region, product, quantity, price, ordered_on, created_at, paid, customer, notes, data) VALUES
              ('Auckland',     'Phone',  2, 500.00, '2026-01-15', '2026-01-15T10:00:00Z', true,  '11111111-1111-1111-1111-111111111111', 'rush order',    '{"k":1}'),
              ('Auckland',     'Laptop', 1, 1500.00,'2026-01-20', '2026-01-20T11:00:00Z', false, '22222222-2222-2222-2222-222222222222', NULL,            '{"a":1}'),
              ('Wellington',   'Phone',  3, 500.00, '2026-02-03', '2026-02-03T09:30:00Z', true,  '11111111-1111-1111-1111-111111111111', '50%_off\ sale', NULL),
              ('Wellington',   'Phone',  1, 450.00, '2026-02-17', '2026-02-17T23:30:00Z', true,  '33333333-3333-3333-3333-333333333333', NULL,            NULL),
              ('Christchurch', 'Tablet', 5, 300.00, '2026-03-05', '2026-03-05T00:00:00Z', false, '22222222-2222-2222-2222-222222222222', NULL,            NULL),
              ('Christchurch', 'Phone',  4, 520.00, '2026-03-10', '2026-03-10T12:00:00Z', true,  '11111111-1111-1111-1111-111111111111', 'rush order',    NULL),
              ('Auckland',     'Phone',  1, 480.00, '2026-03-28', '2026-03-28T08:00:00Z', true,  NULL,                                   NULL,            NULL),
              ('Dunedin',      NULL,  NULL, 100.00, '2026-03-31', '2026-03-31T23:59:59Z', true,  NULL,                                   NULL,            NULL);

            CREATE ROLE reader LOGIN PASSWORD '{{ReaderPassword}}';
            GRANT SELECT ON sales, big_sales, orders TO reader;
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Connection string for the seeded database, as the limited <c>reader</c> role (or any role).</summary>
    public string TargetConnectionString(string username = "reader", string? password = ReaderPassword, string? host = null, int? port = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = username,
            Password = password,
        };
        if (host is not null) builder.Host = host;
        if (port is not null) builder.Port = port.Value;
        return builder.ConnectionString;
    }

    /// <summary>Runs SQL as the database owner: to set up or change data behind the API's back (the API's role can only read).</summary>
    public async Task ExecuteAsOwnerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs a query as the database owner, for assertions about what the API did (or did not do) to the data.</summary>
    public async Task<long> CountSalesAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM sales", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                ["Jwt:Key"] = "integration-test-key-integration-test-key-0123456789",
                ["RateLimiting:AuthPermitLimit"] = "10000",
                ["RateLimiting:ConnectionPermitLimit"] = "10000",
                ["RateLimiting:SharedPermitLimit"] = "10000",
                // Tests run refresh passes themselves, at the moments they choose; a background loop would race them.
                ["Refresh:Enabled"] = "false",
                // The test database is on localhost, which is blocked by default; tests of the block turn it back off.
                ["DataSourceSecurity:AllowPrivateNetworks"] = "true",
                ["Uploads:Path"] = UploadFolder,
            }));
    }

    /// <summary>Where uploaded CSV files go during the tests; removed with the factory.</summary>
    public string UploadFolder { get; } = Path.Combine(Path.GetTempPath(), "databoard-it-" + Guid.NewGuid().ToString("N"));

    public HttpClient CreateApiClient(bool handleCookies = false) =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            // https so the Secure refresh cookie is accepted; cookies are handled manually by the tests that need them.
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = handleCookies,
        });

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _mysql.DisposeAsync();
        await _mssql.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(UploadFolder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
