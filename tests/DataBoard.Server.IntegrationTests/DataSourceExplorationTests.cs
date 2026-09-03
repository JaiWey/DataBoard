using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DataSourceExplorationTests(ApiFactory factory)
{
    private sealed record TestResult(bool Success, string Message, string? ServerVersion, long LatencyMs);
    private sealed record Column(string Name, string DataType, bool Nullable);
    private sealed record Table(string Name, string Kind, List<Column> Columns);
    private sealed record SchemaNode(string Name, List<Table> Tables);
    private sealed record SchemaResult(List<SchemaNode> Schemas, bool Truncated);
    private sealed record PreviewColumn(string Name, string DataType);
    private sealed record PreviewResult(List<PreviewColumn> Columns, List<List<JsonElement>> Rows, bool Truncated);

    private static string Url(Guid org, string suffix = "") => $"/api/organizations/{org}/data-sources{suffix}";

    private async Task<(HttpClient Client, Guid Org, Guid Source)> OwnerWithSourceAsync(string? connectionString = null)
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var response = await client.PostAsJsonAsync(Url(org.Id),
            new { name = "Sales", type = "PostgreSql", connectionString = connectionString ?? factory.TargetConnectionString() });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<DataSourceDto>(Json))!;
        return (client, org.Id, created.Id);
    }

    private static async Task<PreviewResult> ReadPreviewAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PreviewResult>(Json))!;
    }

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, Guid org, Guid source, string sql, int? maxRows = null) =>
        client.PostAsJsonAsync(Url(org, $"/{source}/preview"), new { sql, maxRows });

    // ---- connection test -------------------------------------------------------------------------------------

    [Fact]
    public async Task Test_new_connection_succeeds_with_valid_details()
    {
        var (client, org, _) = await OwnerWithSourceAsync();

        var response = await client.PostAsJsonAsync(Url(org, "/test"),
            new { type = "PostgreSql", connectionString = factory.TargetConnectionString() });

        var result = (await response.Content.ReadFromJsonAsync<TestResult>(Json))!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result.Success);
        Assert.StartsWith("17", result.ServerVersion);
    }

    [Fact]
    public async Task Test_saved_connection_succeeds()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var result = await client.PostAsync(Url(org, $"/{source}/test"), null);

        Assert.True((await result.Content.ReadFromJsonAsync<TestResult>(Json))!.Success);
    }

    [Fact]
    public async Task Wrong_password_is_reported_plainly_without_echoing_secrets()
    {
        var (client, org, _) = await OwnerWithSourceAsync();

        var response = await client.PostAsJsonAsync(Url(org, "/test"),
            new { type = "PostgreSql", connectionString = factory.TargetConnectionString(password: "totally-wrong-pw") });

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TestResult>(body, Json)!;
        Assert.False(result.Success);
        Assert.Contains("Authentication failed", result.Message);
        Assert.DoesNotContain("totally-wrong-pw", body);
        Assert.DoesNotContain(ApiFactory.ReaderPassword, body);
    }

    [Fact]
    public async Task Unreachable_server_is_a_failed_test_not_an_error()
    {
        var (client, org, _) = await OwnerWithSourceAsync();

        var response = await client.PostAsJsonAsync(Url(org, "/test"),
            new { type = "PostgreSql", connectionString = factory.TargetConnectionString(host: "127.0.0.1", port: 1) });

        var result = (await response.Content.ReadFromJsonAsync<TestResult>(Json))!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result.Success);
        Assert.Equal("Could not connect to the server.", result.Message);
    }

    [Fact]
    public async Task Unknown_database_is_reported()
    {
        var (client, org, _) = await OwnerWithSourceAsync();
        var cs = factory.TargetConnectionString() + ";Database=no_such_db";

        var response = await client.PostAsJsonAsync(Url(org, "/test"), new { type = "PostgreSql", connectionString = cs });

        var result = (await response.Content.ReadFromJsonAsync<TestResult>(Json))!;
        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Message);
    }

    [Theory]
    [InlineData(";Passfile=/etc/passwd")]
    [InlineData(";SSL Key=/etc/ssl/private/server.key")]
    [InlineData(";Options=-c default_transaction_read_only=off")]
    public async Task Connection_strings_with_forbidden_options_are_rejected_when_testing_and_when_saving(string extra)
    {
        var (client, org, _) = await OwnerWithSourceAsync();
        var cs = factory.TargetConnectionString() + extra;

        var test = await client.PostAsJsonAsync(Url(org, "/test"), new { type = "PostgreSql", connectionString = cs });
        var save = await client.PostAsJsonAsync(Url(org), new { name = "Bad", type = "PostgreSql", connectionString = cs });

        Assert.Equal(HttpStatusCode.BadRequest, test.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        var list = await client.GetStringAsync(Url(org));
        Assert.DoesNotContain("\"Bad\"", list);
    }

    [Fact]
    public async Task Data_source_types_without_a_connector_cannot_be_created()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var response = await client.PostAsJsonAsync(Url(org.Id),
            new { name = "Mssql", type = "SqlServer", connectionString = "Server=x;Database=d;User Id=u;Password=p" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Internal_addresses_are_blocked_unless_private_networks_are_enabled()
    {
        using var strict = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["DataSourceSecurity:AllowPrivateNetworks"] = "false" })));
        var client = strict.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var email = await RegisterAsync(client);
        var (_, session) = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);
        var org = await CreateOrgAsync(client);

        foreach (var host in new[] { "127.0.0.1", "169.254.169.254", "10.0.0.5", "localhost" })
        {
            var response = await client.PostAsJsonAsync(Url(org.Id, "/test"),
                new { type = "PostgreSql", connectionString = factory.TargetConnectionString(host: host) });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("not allowed", await response.Content.ReadAsStringAsync());
        }
    }

    // ---- schema browsing -------------------------------------------------------------------------------------

    [Fact]
    public async Task Schema_lists_only_what_the_database_account_can_see()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await client.GetAsync(Url(org, $"/{source}/schema"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var schema = (await response.Content.ReadFromJsonAsync<SchemaResult>(Json))!;
        var tables = schema.Schemas.Single(s => s.Name == "public").Tables;

        var sales = tables.Single(t => t.Name == "sales");
        Assert.Equal("Table", sales.Kind);
        Assert.Equal(["id", "region", "amount", "sold_at", "note"], sales.Columns.Select(c => c.Name));
        Assert.Contains(sales.Columns, c => c.Name == "note" && c.Nullable);
        Assert.Contains(sales.Columns, c => c.Name == "region" && !c.Nullable && c.DataType == "text");

        Assert.Equal("View", tables.Single(t => t.Name == "big_sales").Kind);
        Assert.DoesNotContain(tables, t => t.Name == "secret_table"); // no privileges, so not visible
        Assert.DoesNotContain(schema.Schemas, s => s.Name is "pg_catalog" or "information_schema");
        Assert.False(schema.Truncated);
    }

    // ---- query preview ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Preview_returns_columns_rows_and_marks_truncation()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var preview = await ReadPreviewAsync(await PreviewAsync(client, org, source,
            "SELECT id, region, amount FROM sales ORDER BY id -- newest last", maxRows: 10));

        Assert.Equal(["id", "region", "amount"], preview.Columns.Select(c => c.Name));
        Assert.Equal(10, preview.Rows.Count);
        Assert.True(preview.Truncated);
        Assert.Equal("Auckland", preview.Rows[0][1].GetString());
        Assert.Equal(1, preview.Rows[0][0].GetInt32());
    }

    [Fact]
    public async Task Preview_is_not_truncated_when_everything_fits()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var preview = await ReadPreviewAsync(await PreviewAsync(client, org, source, "SELECT * FROM big_sales WHERE amount <= 105"));

        Assert.Equal(5, preview.Rows.Count);
        Assert.False(preview.Truncated);
    }

    [Fact]
    public async Task Preview_caps_rows_at_the_maximum_even_if_more_are_requested()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var preview = await ReadPreviewAsync(await PreviewAsync(client, org, source, "SELECT * FROM sales", maxRows: 100_000));

        Assert.Equal(100, preview.Rows.Count);
        Assert.True(preview.Truncated);
    }

    [Fact]
    public async Task Preview_handles_nulls_and_unusual_types()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var preview = await ReadPreviewAsync(await PreviewAsync(client, org, source,
            "SELECT NULL::text AS n, ARRAY[1,2,3] AS a, 'NaN'::double precision AS d, now() AS t, '\\xdeadbeef'::bytea AS b"));

        var row = preview.Rows.Single();
        Assert.Equal(JsonValueKind.Null, row[0].ValueKind);
        Assert.Equal("{1,2,3}", row[1].GetString());
        Assert.Equal("NaN", row[2].GetString());
        Assert.Equal("\\xDEADBEEF", row[4].GetString());
    }

    [Theory]
    [InlineData("DELETE FROM sales")]
    [InlineData("DROP TABLE sales")]
    [InlineData("INSERT INTO sales (region, amount) VALUES ('x', 1)")]
    [InlineData("SELECT 1; DELETE FROM sales")]
    [InlineData("SELECT * INTO copy_of_sales FROM sales")]
    [InlineData("SELECT pg_read_file('/etc/passwd')")]
    [InlineData("SELECT * FROM pg_ls_dir('/')")]
    [InlineData("SELECT * FROM secret_table FOR UPDATE")]
    [InlineData("WITH i AS (INSERT INTO sales (region, amount) VALUES ('x', 1) RETURNING *) SELECT * FROM i")]
    public async Task Preview_rejects_anything_but_a_safe_select_and_changes_nothing(string sql)
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var before = await factory.CountSalesAsync();

        var response = await PreviewAsync(client, org, source, sql);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await factory.CountSalesAsync());
    }

    [Fact]
    public async Task A_write_hidden_inside_a_function_is_stopped_by_the_read_only_transaction()
    {
        // do_write() passes the SQL validator (it is just a function call), so this proves the second layer.
        var (client, org, source) = await OwnerWithSourceAsync();
        var before = await factory.CountSalesAsync();

        var response = await PreviewAsync(client, org, source, "SELECT do_write()");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("read-only", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await factory.CountSalesAsync());
    }

    [Fact]
    public async Task The_database_account_limits_what_can_be_read()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await PreviewAsync(client, org, source, "SELECT * FROM secret_table");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("permission denied", body);
        Assert.DoesNotContain("do-not-show", body);
    }

    [Fact]
    public async Task Database_syntax_errors_are_returned_so_authors_can_fix_them()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await PreviewAsync(client, org, source, "SELECT nonexistent_column FROM sales");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("nonexistent_column", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Long_running_queries_are_cancelled_by_the_statement_timeout()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await PreviewAsync(client, org, source, "SELECT count(*) FROM generate_series(1, 5000000000)");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("timeout", await response.Content.ReadAsStringAsync());
    }

    // ---- who may do what -------------------------------------------------------------------------------------

    [Fact]
    public async Task Viewers_can_browse_the_schema_but_not_test_or_run_queries()
    {
        var (owner, org, source) = await OwnerWithSourceAsync();
        var (viewer, viewerEmail, _) = await NewUserAsync(factory);
        await owner.PostAsJsonAsync($"/api/organizations/{org}/members", new { email = viewerEmail, role = "Viewer" });

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Url(org, $"/{source}/schema"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync(Url(org, $"/{source}/test"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PreviewAsync(viewer, org, source, "SELECT 1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(Url(org, "/test"),
            new { type = "PostgreSql", connectionString = factory.TargetConnectionString() })).StatusCode);
    }

    [Fact]
    public async Task Editors_can_run_queries()
    {
        var (owner, org, source) = await OwnerWithSourceAsync();
        var (editor, editorEmail, _) = await NewUserAsync(factory);
        await owner.PostAsJsonAsync($"/api/organizations/{org}/members", new { email = editorEmail, role = "Editor" });

        Assert.Equal(HttpStatusCode.OK, (await PreviewAsync(editor, org, source, "SELECT 1")).StatusCode);
    }

    [Fact]
    public async Task Strangers_get_404_and_cannot_reach_another_organizations_data_source()
    {
        var (_, org, source) = await OwnerWithSourceAsync();
        var (stranger, _, _) = await NewUserAsync(factory);
        var strangersOrg = await CreateOrgAsync(stranger, "Mine");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Url(org, $"/{source}/schema"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PreviewAsync(stranger, org, source, "SELECT 1")).StatusCode);

        // Owner of their own organization, guessing the other organization's data source id.
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Url(strangersOrg.Id, $"/{source}/schema"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PreviewAsync(stranger, strangersOrg.Id, source, "SELECT 1")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected()
    {
        var (_, org, source) = await OwnerWithSourceAsync();
        var anonymous = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(org, $"/{source}/schema"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PreviewAsync(anonymous, org, source, "SELECT 1")).StatusCode);
    }

    [Fact]
    public async Task Stored_connection_string_never_appears_in_any_response()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var bodies = new[]
        {
            await client.GetStringAsync(Url(org)),
            await client.GetStringAsync(Url(org, $"/{source}/schema")),
            await (await client.PostAsync(Url(org, $"/{source}/test"), null)).Content.ReadAsStringAsync(),
            await (await PreviewAsync(client, org, source, "SELECT current_user")).Content.ReadAsStringAsync(),
        };

        foreach (var body in bodies)
            Assert.DoesNotContain(ApiFactory.ReaderPassword, body);
    }
}
