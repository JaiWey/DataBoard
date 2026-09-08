using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Dashboards over the fixed "orders" table: Auckland 3 orders, Wellington 2, Christchurch 2, Dunedin 1.</summary>
[Collection(ApiCollection.Name)]
public class DashboardApiTests(ApiFactory factory)
{
    private const string OrdersSql = "SELECT id, region, product, quantity, price, ordered_on FROM orders";
    private const string SalesSql = "SELECT id, region, amount FROM sales WHERE id <= 5";
    private const string NoRegionSql = "SELECT id, product, price FROM orders";

    private sealed record Fixture(HttpClient Owner, Guid Org, Guid Source, Guid Orders);

    private async Task<Fixture> NewFixtureAsync()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var source = await CreateSourceAsync(client, org.Id);
        return new Fixture(client, org.Id, source, await CreateDatasetAsync(client, org.Id, source, "Orders", OrdersSql));
    }

    private async Task<Guid> CreateSourceAsync(HttpClient client, Guid org)
    {
        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/data-sources",
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id;
    }

    private static async Task<Guid> CreateDatasetAsync(HttpClient client, Guid org, Guid source, string name, string sql)
    {
        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/datasets", new { dataSourceId = source, name, sql });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Saves a chart (revenue by region) and returns it, ready to be placed on a dashboard.</summary>
    private static async Task<object> RegionRevenueChartAsync(
        HttpClient client, Guid org, Guid dataset, string title = "Revenue by region", string chartType = "Bar", string column = "price")
    {
        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/charts", new
        {
            name = title,
            datasetId = dataset,
            type = chartType,
            definition = new
            {
                query = new
                {
                    dimensions = new[] { new { column = "region" } },
                    measures = new[] { new { aggregation = "Sum", column, alias = "revenue" } },
                },
            },
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        return new { chartId = id };
    }

    private static object RegionFilter(string label = "Region", string op = "In") => new { label, column = "region", @operator = op };

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid org, string name, object[] widgets, object[]? filters = null) =>
        client.PostAsJsonAsync($"/api/organizations/{org}/dashboards",
            new { name, definition = new { widgets, filters = filters ?? [] } });

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<(HttpClient Client, string Email)> AddMemberAsync(HttpClient owner, Guid org, string role)
    {
        var (member, email, _) = await NewUserAsync(factory);
        (await owner.PostAsJsonAsync($"/api/organizations/{org}/members", new { email, role })).EnsureSuccessStatusCode();
        return (member, email);
    }

    private static Task<HttpResponseMessage> RunAsync(HttpClient client, Guid org, Guid dashboard, object? filters = null) =>
        client.PostAsJsonAsync($"/api/organizations/{org}/dashboards/{dashboard}/run", new { filters });

    // ---- saving -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_assigns_ids_and_round_trips_the_definition()
    {
        var f = await NewFixtureAsync();

        var chart = await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders);
        var created = await ReadAsync(await SaveAsync(f.Owner, f.Org, "Sales overview", [chart], [RegionFilter()]));

        var widget = created.GetProperty("definition").GetProperty("widgets")[0];
        Assert.NotEqual(Guid.Empty, widget.GetProperty("id").GetGuid());
        Assert.NotEqual(Guid.Empty, widget.GetProperty("chartId").GetGuid());
        Assert.Equal("In", created.GetProperty("definition").GetProperty("filters")[0].GetProperty("operator").GetString());

        var id = created.GetProperty("id").GetGuid();
        var fetched = await ReadAsync(await f.Owner.GetAsync($"/api/organizations/{f.Org}/dashboards/{id}"));
        Assert.Equal(widget.GetProperty("id").GetGuid(), fetched.GetProperty("definition").GetProperty("widgets")[0].GetProperty("id").GetGuid());

        var list = await ReadAsync(await f.Owner.GetAsync($"/api/organizations/{f.Org}/dashboards"));
        Assert.Equal(1, list[0].GetProperty("widgetCount").GetInt32());
        Assert.Equal(widget.GetProperty("chartId").GetGuid(), list[0].GetProperty("chartIds")[0].GetGuid());
    }

    [Fact]
    public async Task Duplicate_names_conflict_ignoring_case()
    {
        var f = await NewFixtureAsync();
        (await SaveAsync(f.Owner, f.Org, "Sales", [])).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await SaveAsync(f.Owner, f.Org, "sales", [])).StatusCode);
    }

    [Fact]
    public async Task A_widget_must_refer_to_a_chart_that_exists()
    {
        var f = await NewFixtureAsync();

        var unknown = await SaveAsync(f.Owner, f.Org, "Bad", [new { chartId = Guid.NewGuid() }]);
        var empty = await SaveAsync(f.Owner, f.Org, "Empty", [new { chartId = Guid.Empty }]);

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("chart was not found", await unknown.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task A_chart_can_only_be_on_a_dashboard_once()
    {
        var f = await NewFixtureAsync();
        var chart = await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders);

        var response = await SaveAsync(f.Owner, f.Org, "Twice", [chart, chart]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("only be on a dashboard once", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_widget_cannot_use_a_chart_from_another_organization()
    {
        var f = await NewFixtureAsync();
        var other = await NewFixtureAsync();
        var foreign = await RegionRevenueChartAsync(other.Owner, other.Org, other.Orders);

        var response = await SaveAsync(f.Owner, f.Org, "Sneaky", [foreign]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Several_dashboards_can_show_the_same_chart()
    {
        var f = await NewFixtureAsync();
        var chart = await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders);

        (await SaveAsync(f.Owner, f.Org, "One", [chart])).EnsureSuccessStatusCode();
        (await SaveAsync(f.Owner, f.Org, "Two", [chart])).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Unsupported_filter_operators_are_rejected()
    {
        var f = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SaveAsync(f.Owner, f.Org, "B", [], [RegionFilter(op: "IsNull")])).StatusCode);
    }

    [Fact]
    public async Task Update_replaces_the_definition_and_delete_removes_it()
    {
        var f = await NewFixtureAsync();
        var created = await ReadAsync(await SaveAsync(f.Owner, f.Org, "Sales", [await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders)]));
        var id = created.GetProperty("id").GetGuid();

        var updated = await f.Owner.PutAsJsonAsync($"/api/organizations/{f.Org}/dashboards/{id}",
            new { name = "Sales v2", definition = new { widgets = new object[] { }, filters = new object[] { } } });
        Assert.Equal("Sales v2", (await ReadAsync(updated)).GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await f.Owner.DeleteAsync($"/api/organizations/{f.Org}/dashboards/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.GetAsync($"/api/organizations/{f.Org}/dashboards/{id}")).StatusCode);
    }

    // ---- access -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Viewers_can_open_and_run_but_not_change_dashboards()
    {
        var f = await NewFixtureAsync();
        var id = (await ReadAsync(await SaveAsync(f.Owner, f.Org, "Sales", [await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders)]))).GetProperty("id").GetGuid();
        var (viewer, _) = await AddMemberAsync(f.Owner, f.Org, "Viewer");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/organizations/{f.Org}/dashboards/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RunAsync(viewer, f.Org, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(viewer, f.Org, "Mine", [])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/organizations/{f.Org}/dashboards/{id}")).StatusCode);
    }

    [Fact]
    public async Task Non_members_get_404()
    {
        var f = await NewFixtureAsync();
        var id = (await ReadAsync(await SaveAsync(f.Owner, f.Org, "Sales", []))).GetProperty("id").GetGuid();
        var (stranger, _, _) = await NewUserAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/organizations/{f.Org}/dashboards/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RunAsync(stranger, f.Org, id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/organizations/{f.Org}/dashboards")).StatusCode);
    }

    // ---- running ------------------------------------------------------------------------------------------

    private static List<JsonElement> Widgets(JsonElement run) => run.GetProperty("widgets").EnumerateArray().ToList();

    private static string[] Regions(JsonElement widget) =>
        widget.GetProperty("result").GetProperty("rows").EnumerateArray().Select(r => r[0].GetString()!).ToArray();

    [Fact]
    public async Task Run_returns_each_widgets_rows_and_only_editors_see_the_sql()
    {
        var f = await NewFixtureAsync();
        var id = (await ReadAsync(await SaveAsync(f.Owner, f.Org, "Sales", [await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders)]))).GetProperty("id").GetGuid();
        var (viewer, _) = await AddMemberAsync(f.Owner, f.Org, "Viewer");

        var asOwner = Widgets(await ReadAsync(await RunAsync(f.Owner, f.Org, id)))[0];
        Assert.Equal(["Auckland", "Christchurch", "Dunedin", "Wellington"], Regions(asOwner));
        Assert.Equal(2480m, asOwner.GetProperty("result").GetProperty("rows")[0][1].GetDecimal());
        Assert.NotEqual(JsonValueKind.Null, asOwner.GetProperty("result").GetProperty("sql").ValueKind);

        var asViewer = Widgets(await ReadAsync(await RunAsync(viewer, f.Org, id)))[0];
        Assert.Equal(JsonValueKind.Null, asViewer.GetProperty("result").GetProperty("sql").ValueKind);
    }

    [Fact]
    public async Task A_shared_filter_applies_to_every_widget_that_has_the_column()
    {
        var f = await NewFixtureAsync();
        var sales = await CreateDatasetAsync(f.Owner, f.Org, f.Source, "Sales", SalesSql);
        var noRegion = await CreateDatasetAsync(f.Owner, f.Org, f.Source, "No region", NoRegionSql);
        var countResponse = await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/charts", new
        {
            name = "Order count",
            datasetId = noRegion,
            type = "Table",
            definition = new { query = new { measures = new[] { new { aggregation = "Count", alias = "n" } } } },
        });
        var noRegionWidget = new { chartId = (await ReadAsync(countResponse)).GetProperty("id").GetGuid() };

        var created = await ReadAsync(await SaveAsync(f.Owner, f.Org, "Everything",
            [await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders),
             await RegionRevenueChartAsync(f.Owner, f.Org, sales, "Sales by region", column: "amount"),
             noRegionWidget], [RegionFilter()]));
        var id = created.GetProperty("id").GetGuid();
        var filterId = created.GetProperty("definition").GetProperty("filters")[0].GetProperty("id").GetGuid();

        var run = await ReadAsync(await RunAsync(f.Owner, f.Org, id,
            new Dictionary<Guid, string[]> { [filterId] = ["Auckland", "Dunedin"] }));
        var widgets = Widgets(run);

        Assert.Equal(["Auckland", "Dunedin"], Regions(widgets[0]));
        Assert.Equal(["Auckland"], Regions(widgets[1])); // the sales table only has Auckland rows
        Assert.Equal(filterId, widgets[0].GetProperty("appliedFilters")[0].GetGuid());

        // This dataset has no region column, so the filter is skipped for it rather than failing it.
        Assert.Empty(widgets[2].GetProperty("appliedFilters").EnumerateArray());
        Assert.Equal(8, widgets[2].GetProperty("result").GetProperty("rows")[0][0].GetInt64());
    }

    [Fact]
    public async Task Without_filter_values_nothing_is_filtered()
    {
        var f = await NewFixtureAsync();
        var created = await ReadAsync(await SaveAsync(f.Owner, f.Org, "Sales", [await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders)], [RegionFilter()]));
        var id = created.GetProperty("id").GetGuid();
        var filterId = created.GetProperty("definition").GetProperty("filters")[0].GetProperty("id").GetGuid();

        var run = await ReadAsync(await RunAsync(f.Owner, f.Org, id, new Dictionary<Guid, string[]> { [filterId] = [] }));

        Assert.Equal(4, Regions(Widgets(run)[0]).Length);
    }

    [Fact]
    public async Task A_filter_value_of_the_wrong_type_fails_that_widget_only()
    {
        var f = await NewFixtureAsync();
        var numeric = new { label = "Quantity", column = "quantity", @operator = "Gte" };
        var created = await ReadAsync(await SaveAsync(f.Owner, f.Org, "Sales",
            [await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders)], [numeric]));
        var id = created.GetProperty("id").GetGuid();
        var filterId = created.GetProperty("definition").GetProperty("filters")[0].GetProperty("id").GetGuid();

        var run = await ReadAsync(await RunAsync(f.Owner, f.Org, id,
            new Dictionary<Guid, string[]> { [filterId] = ["lots"] }));

        var widget = Widgets(run)[0];
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("result").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(widget.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Filter_values_are_data_never_sql()
    {
        var f = await NewFixtureAsync();
        var created = await ReadAsync(await SaveAsync(f.Owner, f.Org, "Sales", [await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders)], [RegionFilter(op: "Eq")]));
        var id = created.GetProperty("id").GetGuid();
        var filterId = created.GetProperty("definition").GetProperty("filters")[0].GetProperty("id").GetGuid();

        var run = await ReadAsync(await RunAsync(f.Owner, f.Org, id,
            new Dictionary<Guid, string[]> { [filterId] = ["x'; DROP TABLE orders; --"] }));

        Assert.Empty(Regions(Widgets(run)[0]));
        Assert.Equal(HttpStatusCode.OK, (await f.Owner.PostAsJsonAsync(
            $"/api/organizations/{f.Org}/datasets/{f.Orders}/query",
            new { measures = new[] { new { aggregation = "Count" } } })).StatusCode); // the table is still there
    }

    [Fact]
    public async Task A_deleted_dataset_takes_its_chart_with_it_but_the_dashboard_still_runs()
    {
        var f = await NewFixtureAsync();
        var sales = await CreateDatasetAsync(f.Owner, f.Org, f.Source, "Sales", SalesSql);
        var created = await ReadAsync(await SaveAsync(f.Owner, f.Org, "Both",
            [await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders),
             await RegionRevenueChartAsync(f.Owner, f.Org, sales, "Sales by region", column: "amount")]));
        var id = created.GetProperty("id").GetGuid();

        (await f.Owner.DeleteAsync($"/api/organizations/{f.Org}/datasets/{sales}")).EnsureSuccessStatusCode();
        var widgets = Widgets(await ReadAsync(await RunAsync(f.Owner, f.Org, id)));

        Assert.Equal(4, Regions(widgets[0]).Length);
        Assert.Contains("was deleted", widgets[1].GetProperty("error").GetString());
    }

    [Fact]
    public async Task Deleting_a_chart_takes_it_off_every_dashboard_that_showed_it()
    {
        var f = await NewFixtureAsync();
        var keep = await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders, "Keep");
        var drop = await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders, "Drop");
        var one = (await ReadAsync(await SaveAsync(f.Owner, f.Org, "One", [keep, drop]))).GetProperty("id").GetGuid();
        var two = (await ReadAsync(await SaveAsync(f.Owner, f.Org, "Two", [drop]))).GetProperty("id").GetGuid();
        var dropId = JsonSerializer.SerializeToElement(drop).GetProperty("chartId").GetGuid();

        (await f.Owner.DeleteAsync($"/api/organizations/{f.Org}/charts/{dropId}")).EnsureSuccessStatusCode();

        var first = await ReadAsync(await f.Owner.GetAsync($"/api/organizations/{f.Org}/dashboards/{one}"));
        var second = await ReadAsync(await f.Owner.GetAsync($"/api/organizations/{f.Org}/dashboards/{two}"));
        Assert.Equal(1, first.GetProperty("definition").GetProperty("widgets").GetArrayLength());
        Assert.Equal(0, second.GetProperty("definition").GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task Changing_a_chart_changes_every_dashboard_that_shows_it()
    {
        var f = await NewFixtureAsync();
        var chart = await RegionRevenueChartAsync(f.Owner, f.Org, f.Orders, "Revenue");
        var chartId = JsonSerializer.SerializeToElement(chart).GetProperty("chartId").GetGuid();
        var id = (await ReadAsync(await SaveAsync(f.Owner, f.Org, "Sales", [chart]))).GetProperty("id").GetGuid();

        (await f.Owner.PutAsJsonAsync($"/api/organizations/{f.Org}/charts/{chartId}", new
        {
            name = "Revenue",
            datasetId = f.Orders,
            type = "Bar",
            definition = new { query = new { dimensions = new[] { new { column = "product" } }, measures = new[] { new { aggregation = "Count", alias = "n" } } } },
        })).EnsureSuccessStatusCode();

        var rows = Widgets(await ReadAsync(await RunAsync(f.Owner, f.Org, id)))[0].GetProperty("result").GetProperty("columns");
        Assert.Equal("product", rows[0].GetProperty("name").GetString());
    }
}
