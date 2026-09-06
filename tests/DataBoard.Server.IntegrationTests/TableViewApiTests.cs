using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TableViewApiTests(ApiFactory factory)
{
    private sealed record ColumnDto(string Name, string DataType, string Role);
    private sealed record DatasetDto(
        Guid Id, string Name, string? Sql, List<ColumnDto> Columns, string Kind, JsonElement? Definition);
    private sealed record PreviewResult(List<ColumnDto> Columns, List<List<JsonElement>> Rows, bool Truncated, string? Sql);

    private static string Datasets(Guid org, string suffix = "") => $"/api/organizations/{org}/datasets{suffix}";

    private async Task<(HttpClient Client, Guid Org, Guid Source)> OwnerWithSourceAsync()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var response = await client.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        response.EnsureSuccessStatusCode();
        var source = (await response.Content.ReadFromJsonAsync<DataSourceDto>(Json))!;
        return (client, org.Id, source.Id);
    }

    private static object Table(string name, params string[] fields) => new { schema = "public", name, fields };

    /// <summary>orders joined to sales on region: a deliberately wide result that still has a fixed shape.</summary>
    private static object OrdersWithSales() => new
    {
        main = Table("orders", "id", "region", "product"),
        joins = new[]
        {
            new
            {
                type = "Inner",
                table = Table("sales", "id", "amount"),
                on = new[] { new { leftStep = 0, leftField = "region", rightField = "region" } },
            },
        },
    };

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid org, Guid source, string name, object definition) =>
        client.PostAsJsonAsync(Datasets(org), new { dataSourceId = source, name, kind = "TableView", definition });

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<HttpClient> AddMemberAsync(HttpClient owner, Guid org, string role)
    {
        var (member, email, _) = await NewUserAsync(factory);
        (await owner.PostAsJsonAsync($"/api/organizations/{org}/members", new { email, role })).EnsureSuccessStatusCode();
        return member;
    }

    [Fact]
    public async Task Creating_a_table_view_generates_validated_sql_and_describes_its_columns()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var dataset = await ReadAsync<DatasetDto>(await CreateAsync(client, org, source, "Orders with sales", OrdersWithSales()));

        Assert.Equal("TableView", dataset.Kind);
        Assert.Contains("INNER JOIN", dataset.Sql);
        // "id" appears in both tables, so the second one is named after its table.
        Assert.Equal(["id", "region", "product", "sales_id", "amount"], dataset.Columns.Select(c => c.Name));
        Assert.NotNull(dataset.Definition);
    }

    [Fact]
    public async Task A_table_view_can_be_previewed_and_charted_like_any_dataset()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var dataset = await ReadAsync<DatasetDto>(await CreateAsync(client, org, source, "Shop",
            new { main = Table("orders", "region", "price") }));

        var preview = await ReadAsync<PreviewResult>(await client.GetAsync(Datasets(org, $"/{dataset.Id}/preview")));
        Assert.Equal(8, preview.Rows.Count);

        var chart = await ReadAsync<PreviewResult>(await client.PostAsJsonAsync(Datasets(org, $"/{dataset.Id}/query"), new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new[] { new { aggregation = "Sum", column = "price", alias = "revenue" } },
            orderBy = new[] { new { field = "revenue", direction = "Desc" } },
        }));
        Assert.Equal("Auckland", chart.Rows[0][0].GetString()); // 500 + 1500 + 480
    }

    [Fact]
    public async Task Unknown_tables_and_fields_are_rejected_and_nothing_is_saved()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var missingTable = await CreateAsync(client, org, source, "A", new { main = Table("secret_table", "token") });
        var missingField = await CreateAsync(client, org, source, "B", new { main = Table("orders", "nope") });
        var injection = await CreateAsync(client, org, source, "C", new { main = Table("orders\"; DROP TABLE sales; --", "id") });

        Assert.Equal(HttpStatusCode.BadRequest, missingTable.StatusCode);
        Assert.Contains("was not found", await missingTable.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, missingField.StatusCode);
        Assert.Contains("no field 'nope'", await missingField.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, injection.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<DatasetDto>>(Datasets(org), Json))!);
        Assert.Equal(150, await factory.CountSalesAsync());
    }

    [Fact]
    public async Task A_join_without_a_relationship_is_rejected()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await CreateAsync(client, org, source, "Loose", new
        {
            main = Table("orders", "id"),
            joins = new[] { new { type = "Left", table = Table("sales", "amount"), on = Array.Empty<object>() } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("relates to the earlier tables", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Updating_a_table_view_regenerates_its_sql()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var created = await ReadAsync<DatasetDto>(await CreateAsync(client, org, source, "Shop", new { main = Table("orders", "id") }));

        var updated = await ReadAsync<DatasetDto>(await client.PutAsJsonAsync(Datasets(org, $"/{created.Id}"),
            new { name = "Shop", description = (string?)null, definition = new { main = Table("orders", "id", "product") } }));

        Assert.Equal("TableView", updated.Kind);
        Assert.Equal(["id", "product"], updated.Columns.Select(c => c.Name));
        Assert.Contains("\"product\"", updated.Sql);
    }

    [Fact]
    public async Task A_sql_dataset_cannot_be_turned_into_a_table_view_by_an_update()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var created = await ReadAsync<DatasetDto>(await client.PostAsJsonAsync(Datasets(org),
            new { dataSourceId = source, name = "Plain", sql = "SELECT id FROM orders" }));

        var response = await client.PutAsJsonAsync(Datasets(org, $"/{created.Id}"),
            new { name = "Plain", definition = new { main = Table("orders", "id") } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Viewers_can_use_a_table_view_but_never_see_its_sql_or_definition()
    {
        var (owner, org, source) = await OwnerWithSourceAsync();
        await ReadAsync<DatasetDto>(await CreateAsync(owner, org, source, "Shop", new { main = Table("orders", "id") }));
        var viewer = await AddMemberAsync(owner, org, "Viewer");

        var list = (await viewer.GetFromJsonAsync<List<DatasetDto>>(Datasets(org), Json))!;

        var dataset = Assert.Single(list);
        Assert.Null(dataset.Sql);
        Assert.Null(dataset.Definition);
        Assert.Equal("TableView", dataset.Kind);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Datasets(org, $"/{dataset.Id}/preview"))).StatusCode);
    }

    // ---- previewing a definition without saving it ----------------------------------------------------------

    private static string PreviewUrl(Guid org, Guid source) => $"/api/organizations/{org}/data-sources/{source}/table-view/preview";

    [Fact]
    public async Task A_definition_can_be_run_without_saving_and_returns_rows_and_sql()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var result = await ReadAsync<PreviewResult>(await client.PostAsJsonAsync(PreviewUrl(org, source),
            new { definition = new { main = Table("orders", "region", "price") }, maxRows = 3 }));

        Assert.Equal(3, result.Rows.Count);
        Assert.True(result.Truncated);
        Assert.Equal(["region", "price"], result.Columns.Select(c => c.Name));
        Assert.Contains("FROM \"public\".\"orders\"", result.Sql);
        Assert.Empty((await client.GetFromJsonAsync<List<DatasetDto>>(Datasets(org), Json))!);
    }

    [Fact]
    public async Task Running_the_first_step_of_a_join_is_just_the_main_table()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var step0 = await ReadAsync<PreviewResult>(await client.PostAsJsonAsync(PreviewUrl(org, source),
            new { definition = new { main = Table("orders", "id") }, maxRows = 100 }));

        Assert.Equal(8, step0.Rows.Count);
        Assert.DoesNotContain("JOIN", step0.Sql);
    }

    [Fact]
    public async Task Previewing_a_definition_is_for_editors_and_rejects_bad_definitions()
    {
        var (owner, org, source) = await OwnerWithSourceAsync();
        var viewer = await AddMemberAsync(owner, org, "Viewer");

        var forbidden = await viewer.PostAsJsonAsync(PreviewUrl(org, source), new { definition = new { main = Table("orders", "id") } });
        var bad = await owner.PostAsJsonAsync(PreviewUrl(org, source), new { definition = new { main = Table("nope", "id") } });
        var empty = await owner.PostAsJsonAsync(PreviewUrl(org, source), new { definition = (object?)null });

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }
}
