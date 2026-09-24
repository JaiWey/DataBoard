using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Tabs: a dashboard as several pages, each with its own grid, sharing the filters.</summary>
[Collection(ApiCollection.Name)]
public class DashboardTabsApiTests(ApiFactory factory)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private sealed record Fixture(HttpClient Owner, Guid Org, Guid Dashboard, Guid ChartA, Guid ChartB);

    private static string Url(Fixture f, string suffix = "") => $"/api/organizations/{f.Org}/dashboards/{f.Dashboard}{suffix}";

    /// <summary>A dashboard with two charts, one above the other on a single page (no tabs).</summary>
    private async Task<Fixture> NewFixtureAsync()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var source = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        source.EnsureSuccessStatusCode();
        var sourceId = (await source.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id;
        var dataset = await Read(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/datasets",
            new { dataSourceId = sourceId, name = "Orders", sql = "SELECT id, region, price FROM orders" }));
        var datasetId = dataset.GetProperty("id").GetGuid();

        async Task<Guid> MakeChart(string name, string aggregation) => (await Read(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/charts", new
        {
            name, datasetId, type = "Number",
            definition = new { query = new { measures = new[] { new { aggregation, column = "price", alias = "n" } } } },
        }))).GetProperty("id").GetGuid();
        var a = await MakeChart("Revenue", "Sum");
        var b = await MakeChart("Average", "Avg");

        var dashboard = await Read(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/dashboards", new
        {
            name = "Sales",
            definition = new
            {
                widgets = new object[]
                {
                    new { chartId = a, layout = new { x = 0, y = 0, w = 6, h = 5 } },
                    new { chartId = b, layout = new { x = 0, y = 5, w = 6, h = 5 } },
                },
                filters = Array.Empty<object>(),
            },
        }));
        return new Fixture(owner, org.Id, dashboard.GetProperty("id").GetGuid(), a, b);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static JsonElement[] Widgets(JsonElement d) => d.GetProperty("definition").GetProperty("widgets").EnumerateArray().ToArray();
    private static JsonElement[] Tabs(JsonElement d) =>
        d.GetProperty("definition").TryGetProperty("tabs", out var tabs) && tabs.ValueKind == JsonValueKind.Array ? tabs.EnumerateArray().ToArray() : [];
    private static Guid? TabOf(JsonElement widget) => widget.GetProperty("tabId").ValueKind == JsonValueKind.Null ? null : widget.GetProperty("tabId").GetGuid();

    private Task<HttpResponseMessage> SaveAsync(Fixture f, object[] tabs, params object[] widgets) =>
        f.Owner.PutAsJsonAsync(Url(f), new { name = "Sales", definition = new { widgets, tabs, filters = Array.Empty<object>() } });

    private object Chart(Fixture f, Guid chart, int y, Guid? tabId = null, Guid? id = null) =>
        new { id = id ?? Guid.Empty, chartId = chart, tabId, layout = new { x = 0, y, w = 6, h = 5 } };

    [Fact]
    public async Task A_dashboard_has_no_tabs_until_it_is_given_some_and_its_widgets_name_none()
    {
        var f = await NewFixtureAsync();

        var dashboard = await Read(await f.Owner.GetAsync(Url(f)));

        Assert.Empty(Tabs(dashboard));
        Assert.All(Widgets(dashboard), w => Assert.Null(TabOf(w)));
    }

    [Fact]
    public async Task Tabs_are_made_by_saving_the_dashboard_and_widgets_without_a_tab_go_to_the_first()
    {
        var f = await NewFixtureAsync();

        var saved = await Read(await SaveAsync(f, [new { name = " Overview " }, new { name = "Costs" }],
            Chart(f, f.ChartA, 0), Chart(f, f.ChartB, 5)));

        var tabs = Tabs(saved);
        Assert.Equal(["Overview", "Costs"], tabs.Select(t => t.GetProperty("name").GetString()));
        Assert.All(tabs, t => Assert.NotEqual(Guid.Empty, t.GetProperty("id").GetGuid()));
        Assert.All(Widgets(saved), w => Assert.Equal(tabs[0].GetProperty("id").GetGuid(), TabOf(w)));
    }

    [Fact]
    public async Task A_widget_can_be_moved_to_another_tab_and_each_tab_has_its_own_grid()
    {
        var f = await NewFixtureAsync();
        var first = await Read(await SaveAsync(f, [new { name = "Overview" }, new { name = "Costs" }], Chart(f, f.ChartA, 0), Chart(f, f.ChartB, 5)));
        var tabs = Tabs(first).Select(t => t.GetProperty("id").GetGuid()).ToArray();
        var widgets = Widgets(first);

        // The second chart goes to the second tab and takes the top there; the grids of two tabs may use the same cells.
        var saved = await Read(await SaveAsync(f,
            [new { id = tabs[0], name = "Overview" }, new { id = tabs[1], name = "Costs" }],
            Chart(f, f.ChartA, 0, tabs[0], widgets[0].GetProperty("id").GetGuid()),
            Chart(f, f.ChartB, 0, tabs[1], widgets[1].GetProperty("id").GetGuid())));

        var after = Widgets(saved);
        Assert.Equal([tabs[0], tabs[1]], after.Select(w => TabOf(w)!.Value));
        Assert.Equal([0, 0], after.Select(w => w.GetProperty("layout").GetProperty("y").GetInt32()));
        Assert.Equal(tabs, Tabs(saved).Select(t => t.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task A_widget_on_a_tab_that_does_not_exist_goes_to_the_first_tab()
    {
        var f = await NewFixtureAsync();

        var saved = await Read(await SaveAsync(f, [new { name = "One" }, new { name = "Two" }], Chart(f, f.ChartA, 0, Guid.NewGuid())));

        Assert.Equal(Tabs(saved)[0].GetProperty("id").GetGuid(), TabOf(Widgets(saved).Single()));
    }

    [Fact]
    public async Task Taking_a_tab_away_sends_its_widgets_to_the_first_and_taking_all_of_them_away_makes_it_one_page_again()
    {
        var f = await NewFixtureAsync();
        var first = await Read(await SaveAsync(f, [new { name = "One" }, new { name = "Two" }], Chart(f, f.ChartA, 0), Chart(f, f.ChartB, 5)));
        var tabs = Tabs(first).Select(t => t.GetProperty("id").GetGuid()).ToArray();
        var ids = Widgets(first).Select(w => w.GetProperty("id").GetGuid()).ToArray();
        await Read(await SaveAsync(f, [new { id = tabs[0], name = "One" }, new { id = tabs[1], name = "Two" }],
            Chart(f, f.ChartA, 0, tabs[0], ids[0]), Chart(f, f.ChartB, 0, tabs[1], ids[1])));

        var oneLeft = await Read(await SaveAsync(f, [new { id = tabs[0], name = "One" }],
            Chart(f, f.ChartA, 0, tabs[0], ids[0]), Chart(f, f.ChartB, 0, tabs[1], ids[1])));
        Assert.All(Widgets(oneLeft), w => Assert.Equal(tabs[0], TabOf(w)));

        var none = await Read(await SaveAsync(f, [], Chart(f, f.ChartA, 0, tabs[0], ids[0]), Chart(f, f.ChartB, 5, tabs[0], ids[1])));
        Assert.Empty(Tabs(none));
        Assert.All(Widgets(none), w => Assert.Null(TabOf(w)));
    }

    [Fact]
    public async Task A_dashboard_can_have_ten_tabs_and_no_more()
    {
        var f = await NewFixtureAsync();
        object[] Named(int n) => Enumerable.Range(1, n).Select(i => (object)new { name = $"Tab {i}" }).ToArray();

        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(f, Named(10), Chart(f, f.ChartA, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(f, Named(11), Chart(f, f.ChartA, 0))).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_tab_needs_a_name(string name)
    {
        var f = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(f, [new { name }], Chart(f, f.ChartA, 0))).StatusCode);
    }

    [Fact]
    public async Task A_tab_name_has_a_limit_and_two_tabs_cannot_share_a_name_ignoring_case()
    {
        var f = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(f, [new { name = new string('x', 61) }], Chart(f, f.ChartA, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(f, [new { name = new string('x', 60) }], Chart(f, f.ChartA, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(f, [new { name = "Sales" }, new { name = " sales " }], Chart(f, f.ChartA, 0))).StatusCode);
    }

    [Fact]
    public async Task Two_tabs_cannot_share_an_id()
    {
        var f = await NewFixtureAsync();
        var id = Guid.NewGuid();

        var response = await SaveAsync(f, [new { id, name = "One" }, new { id, name = "Two" }], Chart(f, f.ChartA, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Charts_on_every_tab_run_together()
    {
        var f = await NewFixtureAsync();
        var first = await Read(await SaveAsync(f, [new { name = "One" }, new { name = "Two" }], Chart(f, f.ChartA, 0), Chart(f, f.ChartB, 5)));
        var tabs = Tabs(first).Select(t => t.GetProperty("id").GetGuid()).ToArray();
        var ids = Widgets(first).Select(w => w.GetProperty("id").GetGuid()).ToArray();
        await Read(await SaveAsync(f, [new { id = tabs[0], name = "One" }, new { id = tabs[1], name = "Two" }],
            Chart(f, f.ChartA, 0, tabs[0], ids[0]), Chart(f, f.ChartB, 0, tabs[1], ids[1])));

        var run = await Read(await f.Owner.PostAsJsonAsync(Url(f, "/run"), new { filters = new { } }));

        Assert.Equal(2, run.GetProperty("widgets").GetArrayLength());
        Assert.All(run.GetProperty("widgets").EnumerateArray(), w => Assert.Equal(JsonValueKind.Null, w.GetProperty("error").ValueKind));
    }

    [Fact]
    public async Task A_picture_is_added_below_the_widgets_of_its_own_tab_and_to_the_first_tab_by_default()
    {
        var f = await NewFixtureAsync();
        var first = await Read(await SaveAsync(f, [new { name = "One" }, new { name = "Two" }], Chart(f, f.ChartA, 0), Chart(f, f.ChartB, 5)));
        var tabs = Tabs(first).Select(t => t.GetProperty("id").GetGuid()).ToArray();
        var ids = Widgets(first).Select(w => w.GetProperty("id").GetGuid()).ToArray();
        await Read(await SaveAsync(f, [new { id = tabs[0], name = "One" }, new { id = tabs[1], name = "Two" }],
            Chart(f, f.ChartA, 0, tabs[0], ids[0]), Chart(f, f.ChartB, 0, tabs[1], ids[1])));

        MultipartFormDataContent Form(Guid? tab)
        {
            var form = new MultipartFormDataContent { { new ByteArrayContent(Png), "File", "logo.png" } };
            if (tab is not null) form.Add(new StringContent(tab.Value.ToString()), "TabId");
            return form;
        }

        var onSecond = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(tabs[1])));
        var byDefault = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(null)));
        var unknown = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Guid.NewGuid())));

        var pictures = Widgets(unknown).Where(w => w.GetProperty("kind").GetString() == "Image").ToArray();
        Assert.Equal([tabs[1], tabs[0], tabs[0]], pictures.Select(w => TabOf(w)!.Value));
        // Tab Two's chart ends at row 5, so its picture starts there; tab One's chart ends at row 5 and then the first picture (4 high) at 9.
        Assert.Equal([5, 5, 9], pictures.Select(w => w.GetProperty("layout").GetProperty("y").GetInt32()));
        Assert.Equal(1, Widgets(onSecond).Count(w => w.GetProperty("kind").GetString() == "Image"));
        Assert.Equal(2, Widgets(byDefault).Count(w => w.GetProperty("kind").GetString() == "Image"));
    }

    [Fact]
    public async Task A_visitor_with_a_link_gets_the_tabs_and_the_tab_of_each_widget()
    {
        var f = await NewFixtureAsync();
        var first = await Read(await SaveAsync(f, [new { name = "One" }, new { name = "Two" }], Chart(f, f.ChartA, 0), Chart(f, f.ChartB, 5)));
        var tabs = Tabs(first).Select(t => t.GetProperty("id").GetGuid()).ToArray();
        var ids = Widgets(first).Select(w => w.GetProperty("id").GetGuid()).ToArray();
        await Read(await SaveAsync(f, [new { id = tabs[0], name = "One" }, new { id = tabs[1], name = "Two" }],
            Chart(f, f.ChartA, 0, tabs[0], ids[0]), Chart(f, f.ChartB, 0, tabs[1], ids[1])));
        var link = await Read(await f.Owner.PostAsJsonAsync(Url(f, "/shares"), new { expiresInDays = 30 }));

        var shared = await Read(await factory.CreateApiClient().GetAsync($"/api/shared/dashboards/{link.GetProperty("token").GetString()}"));

        Assert.Equal(["One", "Two"], shared.GetProperty("tabs").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal([tabs[0], tabs[1]], shared.GetProperty("widgets").EnumerateArray().Select(w => TabOf(w)!.Value));
    }

    [Fact]
    public async Task Viewers_cannot_make_tabs()
    {
        var f = await NewFixtureAsync();
        var (viewer, email, _) = await NewUserAsync(factory);
        (await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/members", new { email, role = "Viewer" })).EnsureSuccessStatusCode();

        var response = await viewer.PutAsJsonAsync(Url(f), new { name = "Sales", definition = new { widgets = Array.Empty<object>(), tabs = new[] { new { name = "x" } }, filters = Array.Empty<object>() } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
