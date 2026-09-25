using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Saved charts over the fixed "orders" table: Auckland 3 orders, Wellington 2, Christchurch 2, Dunedin 1.</summary>
[Collection(ApiCollection.Name)]
public class ChartApiTests(ApiFactory factory)
{
    private sealed record Fixture(HttpClient Owner, Guid Org, Guid Orders);

    private async Task<Fixture> NewFixtureAsync()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var source = await client.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        source.EnsureSuccessStatusCode();
        var sourceId = (await source.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id;
        var dataset = await client.PostAsJsonAsync($"/api/organizations/{org.Id}/datasets",
            new { dataSourceId = sourceId, name = "Orders", sql = "SELECT id, region, product, quantity, price, ordered_on FROM orders" });
        Assert.True(dataset.IsSuccessStatusCode, await dataset.Content.ReadAsStringAsync());
        var datasetId = JsonDocument.Parse(await dataset.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        return new Fixture(client, org.Id, datasetId);
    }

    private static string Url(Guid org, string suffix = "") => $"/api/organizations/{org}/charts{suffix}";

    private static object Revenue(Guid dataset, string name = "Revenue by region", string type = "Bar", object? style = null, object? extra = null) => new
    {
        name,
        description = "Money per region",
        datasetId = dataset,
        type,
        definition = new
        {
            query = new
            {
                dimensions = new[] { new { column = "region" } },
                measures = new[] { new { aggregation = "Sum", column = "price", alias = "revenue" } },
                orderBy = new[] { new { field = "revenue", direction = "Desc" } },
                limit = 10,
            },
            style,
        },
    };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<HttpClient> AddMemberAsync(HttpClient owner, Guid org, string role)
    {
        var (member, email, _) = await NewUserAsync(factory);
        (await owner.PostAsJsonAsync($"/api/organizations/{org}/members", new { email, role })).EnsureSuccessStatusCode();
        return member;
    }

    // ---- saving -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_round_trips_type_query_and_style()
    {
        var f = await NewFixtureAsync();

        var created = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org),
            Revenue(f.Orders, style: new { legend = false, labels = true, palette = "warm", stack = true })));

        Assert.Equal("Bar", created.GetProperty("type").GetString());
        Assert.Equal(f.Orders, created.GetProperty("datasetId").GetGuid());
        var definition = created.GetProperty("definition");
        Assert.Equal("Sum", definition.GetProperty("query").GetProperty("measures")[0].GetProperty("aggregation").GetString());
        Assert.Equal(10, definition.GetProperty("query").GetProperty("limit").GetInt32());
        Assert.False(definition.GetProperty("style").GetProperty("legend").GetBoolean());
        Assert.True(definition.GetProperty("style").GetProperty("labels").GetBoolean());
        Assert.Equal("warm", definition.GetProperty("style").GetProperty("palette").GetString());
        Assert.True(definition.GetProperty("style").GetProperty("stack").GetBoolean());

        var id = created.GetProperty("id").GetGuid();
        var fetched = await ReadAsync(await f.Owner.GetAsync(Url(f.Org, $"/{id}")));
        Assert.Equal("Revenue by region", fetched.GetProperty("name").GetString());
        var list = await ReadAsync(await f.Owner.GetAsync(Url(f.Org)));
        Assert.Equal(id, list[0].GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("Table")]
    [InlineData("Bar")]
    [InlineData("Line")]
    [InlineData("Pie")]
    [InlineData("Area")]
    [InlineData("BarHorizontal")]
    [InlineData("Donut")]
    [InlineData("Number")]
    public async Task Every_chart_type_can_be_saved(string type)
    {
        var f = await NewFixtureAsync();

        var created = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders, type: type)));

        Assert.Equal(type, created.GetProperty("type").GetString());
    }

    private static object Shaped(Guid dataset, string type, object[] dimensions, object[] measures, object? style = null) => new
    {
        name = $"{type} {Guid.NewGuid():N}",
        datasetId = dataset,
        type,
        definition = new { query = new { dimensions, measures }, style },
    };

    private static readonly object Region = new { column = "region" };
    private static readonly object Product = new { column = "product" };
    private static readonly object Revenue1 = new { aggregation = "Sum", column = "price", alias = "revenue" };
    private static readonly object Quantity = new { aggregation = "Avg", column = "quantity", alias = "quantity" };
    private static readonly object Orders1 = new { aggregation = "Count", alias = "orders" };

    [Fact]
    public async Task A_heatmap_has_two_dimensions_and_one_measure()
    {
        var f = await NewFixtureAsync();

        var created = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Shaped(f.Orders, "Heatmap", [Region, Product], [Revenue1])));
        Assert.Equal("Heatmap", created.GetProperty("type").GetString());

        foreach (var wrong in new[]
        {
            Shaped(f.Orders, "Heatmap", [Region], [Revenue1]),
            Shaped(f.Orders, "Heatmap", [Region, Product], []),
            Shaped(f.Orders, "Heatmap", [Region, Product], [Revenue1, Orders1]),
            Shaped(f.Orders, "Heatmap", [], [Revenue1]),
        })
        {
            var response = await f.Owner.PostAsJsonAsync(Url(f.Org), wrong);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("heatmap", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task A_scatter_chart_has_one_or_two_dimensions_and_two_or_three_measures()
    {
        var f = await NewFixtureAsync();

        foreach (var right in new[]
        {
            Shaped(f.Orders, "Scatter", [Region], [Revenue1, Quantity]),
            Shaped(f.Orders, "Scatter", [Region], [Revenue1, Quantity, Orders1]),            // bubbles
            Shaped(f.Orders, "Scatter", [Region, Product], [Revenue1, Quantity]),            // coloured series
        })
        {
            Assert.Equal("Scatter", (await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), right))).GetProperty("type").GetString());
        }

        foreach (var wrong in new[]
        {
            Shaped(f.Orders, "Scatter", [], [Revenue1, Quantity]),
            Shaped(f.Orders, "Scatter", [Region], [Revenue1]),
            Shaped(f.Orders, "Scatter", [Region], [Revenue1, Quantity, Orders1, new { aggregation = "Max", column = "price", alias = "top" }]),
        })
        {
            var response = await f.Owner.PostAsJsonAsync(Url(f.Org), wrong);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("scatter", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("Bar")]
    [InlineData("BarHorizontal")]
    [InlineData("Line")]
    [InlineData("Area")]
    public async Task A_percent_chart_is_stacked_and_kept_that_way(string type)
    {
        var f = await NewFixtureAsync();

        var created = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org),
            Shaped(f.Orders, type, [Region, Product], [Revenue1], new { percent = true })));

        var style = created.GetProperty("definition").GetProperty("style");
        Assert.True(style.GetProperty("percent").GetBoolean());
        Assert.True(style.GetProperty("stack").GetBoolean()); // 100% is a way of stacking, even when stack was not sent
    }

    [Theory]
    [InlineData("Pie")]
    [InlineData("Donut")]
    [InlineData("Table")]
    [InlineData("Heatmap")]
    public async Task Percent_is_only_for_charts_that_stack(string type)
    {
        var f = await NewFixtureAsync();
        var dimensions = type == "Heatmap" ? new[] { Region, Product } : [Region];

        var response = await f.Owner.PostAsJsonAsync(Url(f.Org), Shaped(f.Orders, type, dimensions, [Revenue1], new { percent = true }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("100%", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_missing_style_gets_the_defaults()
    {
        var f = await NewFixtureAsync();

        var created = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders)));

        var style = created.GetProperty("definition").GetProperty("style");
        Assert.True(style.GetProperty("legend").GetBoolean());
        Assert.False(style.GetProperty("labels").GetBoolean());
        Assert.Equal("default", style.GetProperty("palette").GetString());
        Assert.False(style.GetProperty("stack").GetBoolean());
        Assert.False(style.GetProperty("percent").GetBoolean());
    }

    [Fact]
    public async Task Duplicate_names_conflict_ignoring_case()
    {
        var f = await NewFixtureAsync();
        (await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders))).EnsureSuccessStatusCode();

        var duplicate = await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders, name: "REVENUE BY REGION"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task A_chart_that_could_never_run_is_rejected_with_the_reason()
    {
        var f = await NewFixtureAsync();
        var bad = new
        {
            name = "Broken",
            datasetId = f.Orders,
            type = "Bar",
            definition = new { query = new { measures = new[] { new { aggregation = "Sum", column = "region", alias = "x" } } } },
        };

        var response = await f.Owner.PostAsJsonAsync(Url(f.Org), bad);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("region", await response.Content.ReadAsStringAsync());
        Assert.Empty((await ReadAsync(await f.Owner.GetAsync(Url(f.Org)))).EnumerateArray());
    }

    [Fact]
    public async Task Unknown_datasets_types_and_palettes_are_rejected()
    {
        var f = await NewFixtureAsync();
        var other = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(other.Orders, name: "Foreign"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders, name: "Radar", type: "Radar"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders, name: "Pal", style: new { palette = "neon" }))).StatusCode);
    }

    [Fact]
    public async Task The_look_choices_round_trip_and_have_defaults()
    {
        var f = await NewFixtureAsync();

        var plain = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders, name: "Plain")));
        var defaults = plain.GetProperty("definition").GetProperty("style");
        Assert.True(defaults.GetProperty("grid").GetBoolean());
        Assert.Equal("bottom", defaults.GetProperty("legendPosition").GetString());
        Assert.Equal(2, defaults.GetProperty("lineWidth").GetInt32());
        Assert.Equal(70, defaults.GetProperty("areaOpacity").GetInt32());
        Assert.Equal("name", defaults.GetProperty("pieLabel").GetString());
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("title").ValueKind);

        var styled = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders, name: "Styled", style: new
        {
            title = "Revenue", xAxisTitle = "Region", yAxisTitle = "NZD", grid = false, yMin = 0, yMax = 500, labelRotate = -45,
            colors = new[] { "#1971c2", "#e8590c" }, seriesColors = new Dictionary<string, string> { ["Auckland"] = "#2b8a3e" },
            numberFormat = new { decimals = 1, thousands = true, compact = true, prefix = "$", suffix = "" },
            legendPosition = "right", smooth = true, lineWidth = 4, symbols = false, barWidth = 60, barRadius = 6, areaOpacity = 30,
            innerRadius = 50, rose = true, pieLabel = "name-percent",
        })));

        var style = styled.GetProperty("definition").GetProperty("style");
        Assert.Equal("Revenue", style.GetProperty("title").GetString());
        Assert.False(style.GetProperty("grid").GetBoolean());
        Assert.Equal(500, style.GetProperty("yMax").GetDouble());
        Assert.Equal(-45, style.GetProperty("labelRotate").GetInt32());
        Assert.Equal(["#1971c2", "#e8590c"], style.GetProperty("colors").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal("#2b8a3e", style.GetProperty("seriesColors").GetProperty("Auckland").GetString());
        Assert.Equal(1, style.GetProperty("numberFormat").GetProperty("decimals").GetInt32());
        Assert.True(style.GetProperty("numberFormat").GetProperty("compact").GetBoolean());
        Assert.Equal("$", style.GetProperty("numberFormat").GetProperty("prefix").GetString());
        Assert.Equal("right", style.GetProperty("legendPosition").GetString());
        Assert.Equal(4, style.GetProperty("lineWidth").GetInt32());
        Assert.Equal(60, style.GetProperty("barWidth").GetInt32());
        Assert.Equal(50, style.GetProperty("innerRadius").GetInt32());
        Assert.Equal("name-percent", style.GetProperty("pieLabel").GetString());

        var id = styled.GetProperty("id").GetGuid();
        var fetched = (await ReadAsync(await f.Owner.GetAsync(Url(f.Org, $"/{id}")))).GetProperty("definition").GetProperty("style");
        Assert.Equal("Region", fetched.GetProperty("xAxisTitle").GetString());
    }

    [Theory]
    [InlineData("""{ "title": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" }""")]
    [InlineData("""{ "yMin": 10, "yMax": 5 }""")]
    [InlineData("""{ "labelRotate": 120 }""")]
    [InlineData("""{ "colors": ["red"] }""")]
    [InlineData("""{ "colors": ["#111111","#222222","#333333","#444444","#555555","#666666","#777777","#888888","#999999","#aaaaaa","#bbbbbb","#cccccc","#dddddd"] }""")]
    [InlineData("""{ "seriesColors": { "A": "blue" } }""")]
    [InlineData("""{ "numberFormat": { "decimals": 9 } }""")]
    [InlineData("""{ "numberFormat": { "prefix": "far too long a prefix" } }""")]
    [InlineData("""{ "legendPosition": "middle" }""")]
    [InlineData("""{ "lineWidth": 0 }""")]
    [InlineData("""{ "barWidth": 5 }""")]
    [InlineData("""{ "barRadius": 50 }""")]
    [InlineData("""{ "areaOpacity": 101 }""")]
    [InlineData("""{ "innerRadius": 95 }""")]
    [InlineData("""{ "pieLabel": "everything" }""")]
    public async Task A_look_choice_out_of_range_is_rejected(string styleJson)
    {
        var f = await NewFixtureAsync();
        var style = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(styleJson);

        var response = await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders, name: "Odd", style: style));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await ReadAsync(await f.Owner.GetAsync(Url(f.Org)))).EnumerateArray());
    }

    [Fact]
    public async Task The_finer_style_groups_round_trip_and_default_to_nothing()
    {
        var f = await NewFixtureAsync();

        var plain = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders, name: "Plain")));
        var defaults = plain.GetProperty("definition").GetProperty("style");
        Assert.Equal("solid", defaults.GetProperty("barBorderStyle").GetString());
        Assert.Equal(0, defaults.GetProperty("barBorderWidth").GetInt32());
        Assert.Equal("auto", defaults.GetProperty("labelPosition").GetString());
        Assert.Equal("auto", defaults.GetProperty("legendType").GetString());
        Assert.False(defaults.GetProperty("legendSelector").GetBoolean());
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("xAxis").ValueKind);
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("margin").ValueKind);

        var styled = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders, name: "Styled", style: new
        {
            barBorderStyle = "dashed", barBorderWidth = 2, barBorderColor = "#c92a2a", barGap = 10, barCategoryGap = 40,
            labelPosition = "insideTop", labelFont = new { family = "serif", size = 14, weight = "bold", italic = true, color = "#343a40" },
            legendType = "scroll", legendSelector = true, legendHeight = 120, legendFont = new { size = 11 },
            xAxis = new { show = true, line = false, ticks = false, labels = true, lineColor = "#adb5bd", labelFont = new { size = 10 }, titleFont = new { weight = "bold" } },
            yAxis = new { show = false },
            xSplitLine = new { show = true, color = "#dee2e6", width = 2, lineType = "dotted" },
            ySplitLine = new { lineType = "dashed" },
            margin = new { left = 40, top = 30, right = 20, bottom = 50 },
        })));

        var style = styled.GetProperty("definition").GetProperty("style");
        Assert.Equal("dashed", style.GetProperty("barBorderStyle").GetString());
        Assert.Equal(40, style.GetProperty("barCategoryGap").GetInt32());
        Assert.Equal("insideTop", style.GetProperty("labelPosition").GetString());
        Assert.Equal("serif", style.GetProperty("labelFont").GetProperty("family").GetString());
        Assert.True(style.GetProperty("labelFont").GetProperty("italic").GetBoolean());
        Assert.Equal("scroll", style.GetProperty("legendType").GetString());
        Assert.Equal(120, style.GetProperty("legendHeight").GetInt32());
        Assert.False(style.GetProperty("xAxis").GetProperty("line").GetBoolean());
        Assert.False(style.GetProperty("yAxis").GetProperty("show").GetBoolean());
        Assert.Equal("#adb5bd", style.GetProperty("xAxis").GetProperty("lineColor").GetString());
        Assert.Equal("dotted", style.GetProperty("xSplitLine").GetProperty("lineType").GetString());
        Assert.Equal(JsonValueKind.Null, style.GetProperty("ySplitLine").GetProperty("show").ValueKind);
        Assert.Equal(50, style.GetProperty("margin").GetProperty("bottom").GetInt32());
    }

    [Theory]
    [InlineData("""{ "barBorderStyle": "wavy" }""")]
    [InlineData("""{ "barBorderWidth": 11 }""")]
    [InlineData("""{ "barBorderColor": "red" }""")]
    [InlineData("""{ "barGap": 101 }""")]
    [InlineData("""{ "barCategoryGap": -1 }""")]
    [InlineData("""{ "labelPosition": "middle" }""")]
    [InlineData("""{ "labelFont": { "family": "Comic Sans" } }""")]
    [InlineData("""{ "labelFont": { "size": 7 } }""")]
    [InlineData("""{ "labelFont": { "size": 49 } }""")]
    [InlineData("""{ "labelFont": { "weight": "heavy" } }""")]
    [InlineData("""{ "labelFont": { "color": "#12" } }""")]
    [InlineData("""{ "legendType": "fancy" }""")]
    [InlineData("""{ "legendHeight": 10 }""")]
    [InlineData("""{ "legendHeight": 1001 }""")]
    [InlineData("""{ "legendFont": { "family": "x" } }""")]
    [InlineData("""{ "xAxis": { "lineColor": "blue" } }""")]
    [InlineData("""{ "yAxis": { "labelFont": { "size": 100 } } }""")]
    [InlineData("""{ "xAxis": { "titleFont": { "color": "nope" } } }""")]
    [InlineData("""{ "xSplitLine": { "color": "grey" } }""")]
    [InlineData("""{ "ySplitLine": { "width": 0 } }""")]
    [InlineData("""{ "ySplitLine": { "width": 11 } }""")]
    [InlineData("""{ "xSplitLine": { "lineType": "wavy" } }""")]
    [InlineData("""{ "margin": { "left": -1 } }""")]
    [InlineData("""{ "margin": { "bottom": 301 } }""")]
    public async Task A_finer_style_choice_out_of_range_is_rejected(string styleJson)
    {
        var f = await NewFixtureAsync();
        var style = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(styleJson);

        var response = await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders, name: "Odd", style: style));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await ReadAsync(await f.Owner.GetAsync(Url(f.Org)))).EnumerateArray());
    }
    [Fact]
    public async Task Update_changes_the_chart_and_may_move_it_to_another_dataset()
    {
        var f = await NewFixtureAsync();
        var created = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders)));
        var id = created.GetProperty("id").GetGuid();

        var updated = await ReadAsync(await f.Owner.PutAsJsonAsync(Url(f.Org, $"/{id}"),
            Revenue(f.Orders, name: "Revenue v2", type: "Line")));

        Assert.Equal("Revenue v2", updated.GetProperty("name").GetString());
        Assert.Equal("Line", updated.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Update_and_delete_find_only_charts_of_the_organization()
    {
        var f = await NewFixtureAsync();
        var other = await NewFixtureAsync();
        var foreign = (await ReadAsync(await other.Owner.PostAsJsonAsync(Url(other.Org), Revenue(other.Orders)))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.GetAsync(Url(f.Org, $"/{foreign}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.PutAsJsonAsync(Url(f.Org, $"/{foreign}"), Revenue(f.Orders))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.DeleteAsync(Url(f.Org, $"/{foreign}"))).StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_chart()
    {
        var f = await NewFixtureAsync();
        var id = (await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders)))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await f.Owner.DeleteAsync(Url(f.Org, $"/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.GetAsync(Url(f.Org, $"/{id}"))).StatusCode);
    }

    // ---- access -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Viewers_can_list_open_and_run_charts_but_not_change_them()
    {
        var f = await NewFixtureAsync();
        var id = (await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders)))).GetProperty("id").GetGuid();
        var viewer = await AddMemberAsync(f.Owner, f.Org, "Viewer");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Url(f.Org))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Url(f.Org, $"/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.PostAsync(Url(f.Org, $"/{id}/run"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders, name: "Mine"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync(Url(f.Org, $"/{id}"), Revenue(f.Orders))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync(Url(f.Org, $"/{id}"))).StatusCode);
    }

    [Fact]
    public async Task Non_members_get_404()
    {
        var f = await NewFixtureAsync();
        var id = (await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders)))).GetProperty("id").GetGuid();
        var (stranger, _, _) = await NewUserAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Url(f.Org))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Url(f.Org, $"/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync(Url(f.Org, $"/{id}/run"), null)).StatusCode);
    }

    // ---- running ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Run_returns_the_rows_sorted_and_limited_as_saved_and_only_editors_see_the_sql()
    {
        var f = await NewFixtureAsync();
        var id = (await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders)))).GetProperty("id").GetGuid();
        var viewer = await AddMemberAsync(f.Owner, f.Org, "Viewer");

        var asOwner = await ReadAsync(await f.Owner.PostAsync(Url(f.Org, $"/{id}/run"), null));
        var regions = asOwner.GetProperty("rows").EnumerateArray().Select(r => r[0].GetString()!).ToArray();
        Assert.Equal(["Auckland", "Wellington", "Christchurch", "Dunedin"], regions); // by revenue, descending: 2480, 1400, 1340, 100
        Assert.NotEqual(JsonValueKind.Null, asOwner.GetProperty("sql").ValueKind);

        var asViewer = await ReadAsync(await viewer.PostAsync(Url(f.Org, $"/{id}/run"), null));
        Assert.Equal(JsonValueKind.Null, asViewer.GetProperty("sql").ValueKind);
    }

    [Fact]
    public async Task Run_of_an_unknown_chart_is_404()
    {
        var f = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.PostAsync(Url(f.Org, $"/{Guid.NewGuid()}/run"), null)).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_dataset_deletes_its_charts()
    {
        var f = await NewFixtureAsync();
        var id = (await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f.Org), Revenue(f.Orders)))).GetProperty("id").GetGuid();

        (await f.Owner.DeleteAsync($"/api/organizations/{f.Org}/datasets/{f.Orders}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.GetAsync(Url(f.Org, $"/{id}"))).StatusCode);
    }
}
