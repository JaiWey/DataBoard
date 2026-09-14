using DataBoard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Runs the real API against a throwaway PostgreSQL container (needs Docker).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ReaderPassword = "reader-pw-123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        await SeedTargetDatabaseAsync();
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
            }));
    }

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
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
