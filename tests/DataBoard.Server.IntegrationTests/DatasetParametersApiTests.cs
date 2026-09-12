using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>
/// Dataset parameters against a real database. The "orders" table: Auckland 3 orders (500, 1500, 480), Wellington 2
/// (500, 450), Christchurch 2 (300, 520), Dunedin 1 (100).
/// </summary>
[Collection(ApiCollection.Name)]
public class DatasetParametersApiTests(ApiFactory factory)
{
    private const string ParameterSql = "SELECT region, product, price FROM orders WHERE region = {{region}} AND price >= {{min_price}}";

    private static readonly object[] RegionAndPrice =
    [
        new { name = "region", type = "Text", @default = "Auckland", label = "Region" },
        new { name = "min_price", type = "Number", @default = 0 },
    ];

    private sealed record Fixture(HttpClient Owner, Guid Org, Guid Source);

    private async Task<Fixture> NewFixtureAsync()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var response = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        response.EnsureSuccessStatusCode();
        return new Fixture(owner, org.Id, (await response.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> CreateDatasetAsync(Fixture f, string sql = ParameterSql, object? parameters = null, string name = "Orders by region") =>
        f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/datasets", new { dataSourceId = f.Source, name, sql, parameters = parameters ?? RegionAndPrice });

    private static async Task<Guid> NewDatasetAsync(Fixture f, string sql = ParameterSql, object? parameters = null, string name = "Orders by region") =>
        (await ReadAsync(await CreateDatasetAsync(f, sql, parameters, name))).GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> QueryAsync(Fixture f, Guid dataset, object query) =>
        f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/datasets/{dataset}/query", query);

    private static async Task<List<string>> RunRegionsAsync(Fixture f, Guid dataset, object? parameters = null)
    {
        var result = await ReadAsync(await QueryAsync(f, dataset, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new[] { new { aggregation = "Count", alias = "n" } },
            parameters,
        }));
        return result.GetProperty("rows").EnumerateArray().Select(r => r[0].GetString()!).ToList();
    }

    // ---- defining them -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dataset_keeps_its_sql_with_the_placeholders_and_its_parameters_with_their_defaults()
    {
        var f = await NewFixtureAsync();

        var created = await ReadAsync(await CreateDatasetAsync(f));

        Assert.Equal(ParameterSql, created.GetProperty("sql").GetString());
        Assert.Equal(["region", "product", "price"], created.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        var parameters = created.GetProperty("parameters").EnumerateArray().ToList();
        Assert.Equal(["region", "min_price"], parameters.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal("Text", parameters[0].GetProperty("type").GetString());
        Assert.Equal("Auckland", parameters[0].GetProperty("default").GetString());
        Assert.Equal("Region", parameters[0].GetProperty("label").GetString());
        Assert.Equal(0, parameters[1].GetProperty("default").GetInt32());

        var fetched = await ReadAsync(await f.Owner.GetAsync($"/api/organizations/{f.Org}/datasets/{created.GetProperty("id").GetGuid()}"));
        Assert.Equal(2, fetched.GetProperty("parameters").GetArrayLength());
    }

    [Fact]
    public async Task A_dataset_without_parameters_is_as_it_always_was()
    {
        var f = await NewFixtureAsync();

        var created = await ReadAsync(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/datasets",
            new { dataSourceId = f.Source, name = "Plain", sql = "SELECT region FROM orders" }));

        Assert.Equal(0, created.GetProperty("parameters").GetArrayLength());
        Assert.Equal(4, (await RunRegionsAsync(f, created.GetProperty("id").GetGuid())).Count);
    }

    [Fact]
    public async Task Editing_a_dataset_can_change_its_parameters()
    {
        var f = await NewFixtureAsync();
        var id = await NewDatasetAsync(f);

        var updated = await ReadAsync(await f.Owner.PutAsJsonAsync($"/api/organizations/{f.Org}/datasets/{id}", new
        {
            name = "Orders by region",
            sql = "SELECT region, price FROM orders WHERE region = {{where}}",
            parameters = new object[] { new { name = "where", type = "Text", @default = "Dunedin" } },
        }));

        Assert.Equal(["where"], updated.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(["Dunedin"], await RunRegionsAsync(f, id));
    }

    [Theory]
    [InlineData("SELECT region FROM orders WHERE region = {{missing}}", "not a declared parameter")]
    [InlineData("SELECT region FROM orders WHERE region = {{region}} AND price > {{min_price}} AND 1 = 1 -- {{region}}", "comment")]
    [InlineData("SELECT region FROM orders WHERE region = '{{region}}' AND price >= {{min_price}}", "quotes")]
    [InlineData("SELECT region FROM orders WHERE region = {{region}}", "'min_price' is not used")]
    public async Task A_dataset_whose_sql_and_parameters_do_not_agree_is_refused_with_the_reason(string sql, string reason)
    {
        var f = await NewFixtureAsync();

        var response = await CreateDatasetAsync(f, sql);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_default_that_does_not_fit_its_type_is_refused()
    {
        var f = await NewFixtureAsync();

        var response = await CreateDatasetAsync(f, parameters: new object[]
        {
            new { name = "region", type = "Text", @default = "Auckland" },
            new { name = "min_price", type = "Number", @default = "expensive" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("expected a number", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_parameter_with_no_default_cannot_be_saved_because_the_sql_could_not_be_checked()
    {
        var f = await NewFixtureAsync();

        var response = await CreateDatasetAsync(f, parameters: new object[]
        {
            new { name = "region", type = "Text" },
            new { name = "min_price", type = "Number", @default = 0 },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'region' needs a value", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_table_view_cannot_have_parameters()
    {
        var f = await NewFixtureAsync();

        var response = await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/datasets", new
        {
            dataSourceId = f.Source,
            name = "View",
            kind = "TableView",
            definition = new { main = new { schema = "public", name = "orders", fields = new[] { "region" } }, joins = Array.Empty<object>() },
            parameters = new object[] { new { name = "a", type = "Text", @default = "x" } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no parameters", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Placeholder_sql_is_still_held_to_the_read_only_rules()
    {
        var f = await NewFixtureAsync();

        var response = await CreateDatasetAsync(f, "SELECT do_write() WHERE {{region}} = {{min_price}}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- running them ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_run_uses_the_defaults_unless_the_query_chooses_other_values()
    {
        var f = await NewFixtureAsync();
        var id = await NewDatasetAsync(f);

        Assert.Equal(["Auckland"], await RunRegionsAsync(f, id));
        Assert.Equal(["Wellington"], await RunRegionsAsync(f, id, new { region = "Wellington" }));
        Assert.Equal(["Christchurch"], await RunRegionsAsync(f, id, new { region = "Christchurch", min_price = 400 }));
        Assert.Empty(await RunRegionsAsync(f, id, new { region = "Dunedin", min_price = "200" })); // a number given as text
    }

    [Fact]
    public async Task Parameters_and_filters_work_together()
    {
        var f = await NewFixtureAsync();
        var id = await NewDatasetAsync(f);

        var result = await ReadAsync(await QueryAsync(f, id, new
        {
            dimensions = new[] { new { column = "product" } },
            measures = new[] { new { aggregation = "Sum", column = "price", alias = "revenue" } },
            filters = new[] { new { column = "product", @operator = "Eq", value = "Phone" } },
            parameters = new { region = "Wellington" },
        }));

        var row = Assert.Single(result.GetProperty("rows").EnumerateArray());
        Assert.Equal("Phone", row[0].GetString());
        Assert.Equal(950m, row[1].GetDecimal()); // Wellington's two phones: 500 + 450
    }

    [Fact]
    public async Task The_sql_shown_to_editors_has_bound_parameters_and_never_the_values()
    {
        var f = await NewFixtureAsync();
        var id = await NewDatasetAsync(f);

        var result = await ReadAsync(await QueryAsync(f, id, new
        {
            measures = new[] { new { aggregation = "Count", alias = "n" } },
            parameters = new { region = "Wellington" },
        }));

        var sql = result.GetProperty("sql").GetString()!;
        Assert.Contains("@databoard_p0", sql);
        Assert.DoesNotContain("Wellington", sql);
        Assert.DoesNotContain("{{", sql);
    }

    [Theory]
    [InlineData("'; DROP TABLE orders; --")]
    [InlineData("Auckland' OR '1'='1")]
    [InlineData("%")]
    [InlineData("{{min_price}}")]
    public async Task A_hostile_value_is_just_text_that_matches_nothing_and_harms_nothing(string hostile)
    {
        var f = await NewFixtureAsync();
        var id = await NewDatasetAsync(f);

        var regions = await RunRegionsAsync(f, id, new { region = hostile });

        Assert.Empty(regions);
        // Nothing was dropped or changed: the same dataset still answers as before.
        Assert.Equal(["Auckland"], await RunRegionsAsync(f, id));
    }

    [Fact]
    public async Task Date_and_true_false_parameters_work_against_the_database()
    {
        var f = await NewFixtureAsync();
        var id = await NewDatasetAsync(f,
            "SELECT region, price FROM orders WHERE ordered_on >= {{since}} AND paid = {{paid}}",
            new object[]
            {
                new { name = "since", type = "Date", @default = "2026-03-01" },
                new { name = "paid", type = "Boolean", @default = true },
            },
            "Recent orders");

        // From 1 March, paid: Christchurch (Mar 10), Auckland (Mar 28), Dunedin (Mar 31).
        Assert.Equal(["Auckland", "Christchurch", "Dunedin"], await RunRegionsAsync(f, id));
        // From February, unpaid: Auckland's laptop (Jan 20) is too early, Christchurch's tablet (Mar 5) qualifies.
        Assert.Equal(["Christchurch"], await RunRegionsAsync(f, id, new { since = "2026-02-01", paid = false }));
        // From February, paid (true given as text): every region has a paid order since then.
        Assert.Equal(["Auckland", "Christchurch", "Dunedin", "Wellington"], await RunRegionsAsync(f, id, new { since = "2026-02-01", paid = "true" }));
    }

    [Fact]
    public async Task A_list_can_be_given_as_text_and_split_in_the_sql()
    {
        var f = await NewFixtureAsync();
        var id = await NewDatasetAsync(f,
            "SELECT region, price FROM orders WHERE region = ANY(string_to_array({{names}}, ','))",
            new object[] { new { name = "names", type = "Text", @default = "Auckland,Dunedin" } },
            "Chosen regions");

        Assert.Equal(["Auckland", "Dunedin"], await RunRegionsAsync(f, id));
        Assert.Equal(["Christchurch", "Wellington"], await RunRegionsAsync(f, id, new { names = "Wellington,Christchurch" }));
    }

    [Fact]
    public async Task A_value_of_the_wrong_type_is_refused_with_the_name_of_the_parameter()
    {
        var f = await NewFixtureAsync();
        var id = await NewDatasetAsync(f);

        var response = await QueryAsync(f, id, new { measures = new[] { new { aggregation = "Count" } }, parameters = new { min_price = "lots" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'min_price': expected a number", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_value_for_a_parameter_the_dataset_does_not_have_is_refused()
    {
        var f = await NewFixtureAsync();
        var id = await NewDatasetAsync(f);

        var response = await QueryAsync(f, id, new { measures = new[] { new { aggregation = "Count" } }, parameters = new { colour = "red" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'colour' is not a parameter", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Previewing_the_sql_before_saving_runs_it_with_the_defaults()
    {
        var f = await NewFixtureAsync();

        var preview = await ReadAsync(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/data-sources/{f.Source}/preview",
            new { sql = ParameterSql, parameters = RegionAndPrice }));

        Assert.Equal(["Auckland"], preview.GetProperty("rows").EnumerateArray().Select(r => r[0].GetString()).Distinct());
        Assert.Equal(3, preview.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task Previewing_a_saved_dataset_uses_the_defaults()
    {
        var f = await NewFixtureAsync();
        var id = await NewDatasetAsync(f);

        var preview = await ReadAsync(await f.Owner.GetAsync($"/api/organizations/{f.Org}/datasets/{id}/preview"));

        Assert.Equal(3, preview.GetProperty("rows").GetArrayLength());
    }

    // ---- charts and dashboards ---------------------------------------------------------------------------------

    private static async Task<Guid> NewChartAsync(Fixture f, Guid dataset, object? parameters = null, string name = "Count")
    {
        var response = await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/charts", new
        {
            name,
            datasetId = dataset,
            type = "Number",
            definition = new { query = new { measures = new[] { new { aggregation = "Count", alias = "n" } }, parameters } },
        });
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<long> RunChartAsync(Fixture f, Guid chart)
    {
        var result = await ReadAsync(await f.Owner.PostAsync($"/api/organizations/{f.Org}/charts/{chart}/run", null));
        return result.GetProperty("rows")[0][0].GetInt64();
    }

    [Fact]
    public async Task A_chart_can_fix_the_values_of_its_datasets_parameters()
    {
        var f = await NewFixtureAsync();
        var dataset = await NewDatasetAsync(f);

        var byDefault = await NewChartAsync(f, dataset, name: "Auckland orders");
        var wellington = await NewChartAsync(f, dataset, new { region = "Wellington" }, "Wellington orders");

        Assert.Equal(3, await RunChartAsync(f, byDefault));
        Assert.Equal(2, await RunChartAsync(f, wellington));
        var saved = await ReadAsync(await f.Owner.GetAsync($"/api/organizations/{f.Org}/charts/{wellington}"));
        Assert.Equal("Wellington", saved.GetProperty("definition").GetProperty("query").GetProperty("parameters").GetProperty("region").GetString());
    }

    [Fact]
    public async Task A_chart_with_a_value_that_does_not_fit_cannot_be_saved()
    {
        var f = await NewFixtureAsync();
        var dataset = await NewDatasetAsync(f);

        var response = await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/charts", new
        {
            name = "Bad",
            datasetId = dataset,
            type = "Number",
            definition = new { query = new { measures = new[] { new { aggregation = "Count" } }, parameters = new { min_price = "lots" } } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'min_price'", await response.Content.ReadAsStringAsync());
    }

    private async Task<(Guid Dashboard, Guid FilterId, Guid RegionChart, Guid PlainChart)> NewDashboardAsync(Fixture f, string filterColumn = "region")
    {
        var dataset = await NewDatasetAsync(f);
        var plain = (await ReadAsync(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/datasets",
            new { dataSourceId = f.Source, name = "Plain orders", sql = "SELECT region, price FROM orders" }))).GetProperty("id").GetGuid();
        var withParameter = await NewChartAsync(f, dataset, name: "Orders in a region");
        var withoutParameter = await NewChartAsync(f, plain, name: "All orders");

        var dashboard = await ReadAsync(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/dashboards", new
        {
            name = "Regions",
            definition = new
            {
                widgets = new object[] { new { chartId = withParameter }, new { chartId = withoutParameter } },
                filters = new object[] { new { label = "Region", column = filterColumn, @operator = "Eq", target = "Parameter" } },
            },
        }));
        return (dashboard.GetProperty("id").GetGuid(), dashboard.GetProperty("definition").GetProperty("filters")[0].GetProperty("id").GetGuid(), withParameter, withoutParameter);
    }

    private static async Task<List<JsonElement>> RunDashboardAsync(Fixture f, Guid dashboard, Guid filter, string? value)
    {
        var filters = value is null ? new Dictionary<Guid, string[]>() : new Dictionary<Guid, string[]> { [filter] = [value] };
        var run = await ReadAsync(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/dashboards/{dashboard}/run", new { filters }));
        return run.GetProperty("widgets").EnumerateArray().ToList();
    }

    [Fact]
    public async Task A_dashboard_filter_can_set_a_parameter_for_the_charts_that_have_it()
    {
        var f = await NewFixtureAsync();
        var (dashboard, filter, _, _) = await NewDashboardAsync(f);

        var untouched = await RunDashboardAsync(f, dashboard, filter, null);
        var wellington = await RunDashboardAsync(f, dashboard, filter, "Wellington");

        Assert.Equal(3, untouched[0].GetProperty("result").GetProperty("rows")[0][0].GetInt64()); // the default, Auckland
        Assert.Equal(2, wellington[0].GetProperty("result").GetProperty("rows")[0][0].GetInt64());
        Assert.Equal(filter, wellington[0].GetProperty("appliedFilters")[0].GetGuid());
        // A chart whose dataset has no such parameter ignores the filter and is not counted as filtered.
        Assert.Equal(8, wellington[1].GetProperty("result").GetProperty("rows")[0][0].GetInt64());
        Assert.Empty(wellington[1].GetProperty("appliedFilters").EnumerateArray());
    }

    [Fact]
    public async Task A_dashboard_filter_value_that_does_not_fit_the_parameter_fails_that_chart_only()
    {
        var f = await NewFixtureAsync();
        var (dashboard, filter, _, _) = await NewDashboardAsync(f, filterColumn: "min_price");

        var widgets = await RunDashboardAsync(f, dashboard, filter, "lots");

        Assert.Contains("'min_price'", widgets[0].GetProperty("error").GetString());
        Assert.Equal(8, widgets[1].GetProperty("result").GetProperty("rows")[0][0].GetInt64());
    }

    [Fact]
    public async Task A_parameter_filter_has_to_be_an_is_condition()
    {
        var f = await NewFixtureAsync();
        var dataset = await NewDatasetAsync(f);
        var chart = await NewChartAsync(f, dataset);

        var response = await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/dashboards", new
        {
            name = "Bad filter",
            definition = new
            {
                widgets = new object[] { new { chartId = chart } },
                filters = new object[] { new { label = "Region", column = "region", @operator = "In", target = "Parameter" } },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sets one value", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_visitor_with_a_share_link_can_use_a_parameter_filter_and_the_column_name_stays_private()
    {
        var f = await NewFixtureAsync();
        var (dashboard, filter, _, _) = await NewDashboardAsync(f);
        var link = await ReadAsync(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/dashboards/{dashboard}/shares", new { expiresInDays = 1 }));
        var token = link.GetProperty("token").GetString()!;
        var visitor = factory.CreateApiClient();

        var page = await visitor.GetStringAsync($"/api/shared/dashboards/{token}");
        var run = await ReadAsync(await visitor.PostAsJsonAsync($"/api/shared/dashboards/{token}/run",
            new { filters = new Dictionary<Guid, string[]> { [filter] = ["Wellington"] } }));

        Assert.DoesNotContain("min_price", page);
        Assert.DoesNotContain("\"target\"", page); // what the filter acts on is not for the public
        Assert.Equal(2, run.GetProperty("widgets")[0].GetProperty("result").GetProperty("rows")[0][0].GetInt64());
    }
}
