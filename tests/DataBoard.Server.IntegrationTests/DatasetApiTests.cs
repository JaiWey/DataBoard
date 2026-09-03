using System.Net;
using System.Net.Http.Json;
using DataBoard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DatasetApiTests(ApiFactory factory)
{
    private sealed record ColumnDto(string Name, string DataType, string Role);
    private sealed record DatasetDto(
        Guid Id, Guid DataSourceId, string Name, string? Description, string? Sql, List<ColumnDto> Columns,
        DateTime CreatedUtc, DateTime UpdatedUtc);
    private sealed record PreviewResult(List<object> Columns, List<List<System.Text.Json.JsonElement>> Rows, bool Truncated);

    private static string Url(Guid org, string suffix = "") => $"/api/organizations/{org}/datasets{suffix}";

    private async Task<(HttpClient Client, Guid Org, Guid Source)> OwnerWithSourceAsync()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var response = await client.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Sales DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        response.EnsureSuccessStatusCode();
        var source = (await response.Content.ReadFromJsonAsync<DataSourceDto>(Json))!;
        return (client, org.Id, source.Id);
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid org, Guid source, string name, string sql) =>
        client.PostAsJsonAsync(Url(org), new { dataSourceId = source, name, description = "test", sql });

    private static async Task<DatasetDto> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DatasetDto>(Json))!;
    }

    private async Task<(HttpClient Viewer, string Email)> AddMemberAsync(HttpClient owner, Guid org, string role)
    {
        var (member, email, _) = await NewUserAsync(factory);
        var response = await owner.PostAsJsonAsync($"/api/organizations/{org}/members", new { email, role });
        response.EnsureSuccessStatusCode();
        return (member, email);
    }

    // ---- create --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_runs_the_query_to_learn_its_columns_and_classifies_them()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var dataset = await ReadAsync(await CreateAsync(client, org, source, "Sales by id",
            "SELECT id, region, amount, sold_at FROM sales;  "));

        Assert.Equal("SELECT id, region, amount, sold_at FROM sales", dataset.Sql); // normalised
        Assert.Equal(
            [("id", "integer", "Measure"), ("region", "text", "Dimension"),
             ("amount", "numeric(10, 2)", "Measure"), ("sold_at", "timestamp with time zone", "Dimension")],
            dataset.Columns.Select(c => (c.Name, c.DataType, c.Role)));
        Assert.Equal(source, dataset.DataSourceId);
    }

    [Fact]
    public async Task Aggregated_and_aliased_columns_are_described_too()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var dataset = await ReadAsync(await CreateAsync(client, org, source, "Totals",
            "SELECT region, count(*) AS orders, sum(amount) AS revenue FROM sales GROUP BY region"));

        Assert.Equal(["region", "orders", "revenue"], dataset.Columns.Select(c => c.Name));
        Assert.Equal("Measure", dataset.Columns.Single(c => c.Name == "orders").Role);
        Assert.Equal("Measure", dataset.Columns.Single(c => c.Name == "revenue").Role);
    }

    [Theory]
    [InlineData("DELETE FROM sales")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT pg_read_file('/etc/passwd')")]
    [InlineData("SELECT * INTO copy_of_sales FROM sales")]
    public async Task Unsafe_sql_is_rejected_and_nothing_is_saved(string sql)
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var before = await factory.CountSalesAsync();

        var response = await CreateAsync(client, org, source, "Bad", sql);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<DatasetDto>>(Url(org), Json))!);
        Assert.Equal(before, await factory.CountSalesAsync());
    }

    [Fact]
    public async Task Database_errors_such_as_unknown_columns_or_missing_privileges_are_reported_and_nothing_is_saved()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var unknown = await CreateAsync(client, org, source, "A", "SELECT nope FROM sales");
        var secret = await CreateAsync(client, org, source, "B", "SELECT * FROM secret_table");

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("nope", await unknown.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, secret.StatusCode);
        Assert.Contains("permission denied", await secret.Content.ReadAsStringAsync());
        Assert.Empty((await client.GetFromJsonAsync<List<DatasetDto>>(Url(org), Json))!);
    }

    [Fact]
    public async Task Duplicate_column_names_ask_the_author_for_aliases()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await CreateAsync(client, org, source, "Dup", "SELECT id, id FROM sales");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("alias", await response.Content.ReadAsStringAsync());

        await ReadAsync(await CreateAsync(client, org, source, "Dup", "SELECT id, id AS id2 FROM sales"));
    }

    [Fact]
    public async Task Names_must_be_unique_per_organization_ignoring_case()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        await ReadAsync(await CreateAsync(client, org, source, "Orders", "SELECT 1 AS one"));

        var duplicate = await CreateAsync(client, org, source, "ORDERS", "SELECT 2 AS two");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // The same name in another organization is fine.
        var (other, otherOrg, otherSource) = await OwnerWithSourceAsync();
        await ReadAsync(await CreateAsync(other, otherOrg, otherSource, "Orders", "SELECT 1 AS one"));
    }

    [Fact]
    public async Task Blank_name_is_400()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(client, org, source, " ", "SELECT 1 AS one")).StatusCode);
    }

    // ---- read and preview ------------------------------------------------------------------------------------

    [Fact]
    public async Task Viewers_can_list_get_and_preview_but_never_see_the_sql()
    {
        var (owner, org, source) = await OwnerWithSourceAsync();
        var created = await ReadAsync(await CreateAsync(owner, org, source, "Orders", "SELECT id, region FROM sales ORDER BY id"));
        var (viewer, _) = await AddMemberAsync(owner, org, "Viewer");

        var listed = (await viewer.GetFromJsonAsync<List<DatasetDto>>(Url(org), Json))!;
        var single = (await viewer.GetFromJsonAsync<DatasetDto>(Url(org, $"/{created.Id}"), Json))!;
        var rawBody = await viewer.GetStringAsync(Url(org, $"/{created.Id}"));
        var preview = (await viewer.GetFromJsonAsync<PreviewResult>(Url(org, $"/{created.Id}/preview?maxRows=5"), Json))!;

        Assert.Null(Assert.Single(listed).Sql);
        Assert.Null(single.Sql);
        Assert.DoesNotContain("FROM sales", rawBody);
        Assert.Equal(["id", "region"], single.Columns.Select(c => c.Name));
        Assert.Equal(5, preview.Rows.Count);
        Assert.True(preview.Truncated);
        Assert.Equal(1, preview.Rows[0][0].GetInt32());

        var asOwner = (await owner.GetFromJsonAsync<DatasetDto>(Url(org, $"/{created.Id}"), Json))!;
        Assert.Equal("SELECT id, region FROM sales ORDER BY id", asOwner.Sql);
    }

    [Fact]
    public async Task Stored_sql_is_validated_again_so_tampering_in_the_database_cannot_run_a_write()
    {
        var (owner, org, source) = await OwnerWithSourceAsync();
        var created = await ReadAsync(await CreateAsync(owner, org, source, "Orders", "SELECT id FROM sales"));
        var before = await factory.CountSalesAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE datasets SET \"Sql\" = 'DELETE FROM sales' WHERE \"Id\" = {0}", created.Id);
        }

        var response = await owner.GetAsync(Url(org, $"/{created.Id}/preview"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await factory.CountSalesAsync());
    }

    // ---- update and delete -----------------------------------------------------------------------------------

    [Fact]
    public async Task Update_changes_the_query_and_the_recorded_columns()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var created = await ReadAsync(await CreateAsync(client, org, source, "Orders", "SELECT id FROM sales"));

        var updated = await ReadAsync(await client.PutAsJsonAsync(Url(org, $"/{created.Id}"),
            new { name = "Orders v2", description = "now with region", sql = "SELECT id, region FROM sales" }));

        Assert.Equal("Orders v2", updated.Name);
        Assert.Equal(["id", "region"], updated.Columns.Select(c => c.Name));
        Assert.True(updated.UpdatedUtc >= created.UpdatedUtc);
        // PostgreSQL keeps microseconds, .NET keeps 100 ns ticks, so compare with a small tolerance.
        Assert.True((updated.CreatedUtc - created.CreatedUtc).Duration() < TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Update_with_bad_sql_or_a_taken_name_changes_nothing()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var first = await ReadAsync(await CreateAsync(client, org, source, "First", "SELECT id FROM sales"));
        await ReadAsync(await CreateAsync(client, org, source, "Second", "SELECT region FROM sales"));

        var bad = await client.PutAsJsonAsync(Url(org, $"/{first.Id}"), new { name = "First", sql = "DROP TABLE sales" });
        var taken = await client.PutAsJsonAsync(Url(org, $"/{first.Id}"), new { name = "second", sql = "SELECT id FROM sales" });

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        var current = (await client.GetFromJsonAsync<DatasetDto>(Url(org, $"/{first.Id}"), Json))!;
        Assert.Equal("First", current.Name);
        Assert.Equal("SELECT id FROM sales", current.Sql);
    }

    [Fact]
    public async Task Delete_removes_the_dataset()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var created = await ReadAsync(await CreateAsync(client, org, source, "Orders", "SELECT id FROM sales"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Url(org, $"/{created.Id}"))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(org, $"/{created.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(Url(org, $"/{created.Id}"))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<DatasetDto>>(Url(org), Json))!);
    }

    // ---- who may do what -------------------------------------------------------------------------------------

    [Fact]
    public async Task Viewers_cannot_create_update_or_delete_and_editors_can()
    {
        var (owner, org, source) = await OwnerWithSourceAsync();
        var created = await ReadAsync(await CreateAsync(owner, org, source, "Orders", "SELECT id FROM sales"));
        var (viewer, _) = await AddMemberAsync(owner, org, "Viewer");
        var (editor, _) = await AddMemberAsync(owner, org, "Editor");

        Assert.Equal(HttpStatusCode.Forbidden, (await CreateAsync(viewer, org, source, "V", "SELECT 1 AS a")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PutAsJsonAsync(Url(org, $"/{created.Id}"), new { name = "x", sql = "SELECT 1 AS a" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync(Url(org, $"/{created.Id}"))).StatusCode);

        var byEditor = await ReadAsync(await CreateAsync(editor, org, source, "E", "SELECT 1 AS a"));
        Assert.NotNull(byEditor.Sql);
        Assert.Equal(HttpStatusCode.NoContent, (await editor.DeleteAsync(Url(org, $"/{byEditor.Id}"))).StatusCode);
    }

    [Fact]
    public async Task Other_organizations_cannot_see_or_use_datasets_and_data_sources()
    {
        var (owner, org, source) = await OwnerWithSourceAsync();
        var created = await ReadAsync(await CreateAsync(owner, org, source, "Orders", "SELECT id FROM sales"));
        var (stranger, _, _) = await NewUserAsync(factory);
        var strangersOrg = await CreateOrgAsync(stranger, "Mine");

        // Not a member of the organization at all.
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Url(org))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Url(org, $"/{created.Id}/preview"))).StatusCode);

        // Owner of their own organization, guessing ids from the other one.
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Url(strangersOrg.Id, $"/{created.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Url(strangersOrg.Id, $"/{created.Id}/preview"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync(Url(strangersOrg.Id, $"/{created.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CreateAsync(stranger, strangersOrg.Id, source, "Steal", "SELECT id FROM sales")).StatusCode);

        Assert.Single((await owner.GetFromJsonAsync<List<DatasetDto>>(Url(org), Json))!);
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected()
    {
        var (_, org, _) = await OwnerWithSourceAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateApiClient().GetAsync(Url(org))).StatusCode);
    }
}
