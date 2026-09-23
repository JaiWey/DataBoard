using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Text on the dashboard grid: titles and notes next to charts and pictures.</summary>
[Collection(ApiCollection.Name)]
public class DashboardTextWidgetApiTests(ApiFactory factory)
{
    private sealed record Fixture(HttpClient Owner, Guid Org, Guid Dashboard, Guid Chart);

    private static string Url(Fixture f, string suffix = "") => $"/api/organizations/{f.Org}/dashboards/{f.Dashboard}{suffix}";

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
        var chart = await Read(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/charts", new
        {
            name = "Revenue", datasetId = dataset.GetProperty("id").GetGuid(), type = "Number",
            definition = new { query = new { measures = new[] { new { aggregation = "Sum", column = "price", alias = "revenue" } } } },
        }));
        var chartId = chart.GetProperty("id").GetGuid();
        var dashboard = await Read(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/dashboards", new
        {
            name = "Sales",
            definition = new { widgets = new object[] { new { chartId, layout = new { x = 0, y = 0, w = 6, h = 5 } } }, filters = Array.Empty<object>() },
        }));
        return new Fixture(owner, org.Id, dashboard.GetProperty("id").GetGuid(), chartId);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static object ChartWidget(Fixture f) => new { chartId = f.Chart, layout = new { x = 0, y = 0, w = 6, h = 5 } };

    private Task<HttpResponseMessage> SaveAsync(Fixture f, params object[] widgets) =>
        f.Owner.PutAsJsonAsync(Url(f), new { name = "Sales", definition = new { widgets, filters = Array.Empty<object>() } });

    private static JsonElement[] Texts(JsonElement dashboard) =>
        dashboard.GetProperty("definition").GetProperty("widgets").EnumerateArray().Where(w => w.GetProperty("kind").GetString() == "Text").ToArray();

    [Fact]
    public async Task A_text_widget_is_made_by_saving_the_dashboard_with_defaults_filled_in()
    {
        var f = await NewFixtureAsync();

        var saved = await Read(await SaveAsync(f, ChartWidget(f),
            new { kind = "Text", text = "  # Quarterly numbers  ", layout = new { x = 0, y = 5, w = 12, h = 2 } }));

        var text = Assert.Single(Texts(saved));
        Assert.NotEqual(Guid.Empty, text.GetProperty("id").GetGuid());
        Assert.Equal("# Quarterly numbers", text.GetProperty("text").GetString());
        Assert.Equal("left", text.GetProperty("textAlign").GetString());
        Assert.Equal("normal", text.GetProperty("textSize").GetString());
        Assert.Equal(12, text.GetProperty("layout").GetProperty("w").GetInt32());
        Assert.Equal(2, saved.GetProperty("definition").GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task A_text_widget_keeps_its_id_and_changes_when_saved_again()
    {
        var f = await NewFixtureAsync();
        var first = await Read(await SaveAsync(f, ChartWidget(f), new { kind = "Text", text = "Hello", textAlign = "center", textSize = "large" }));
        var id = Texts(first).Single().GetProperty("id").GetGuid();

        var second = await Read(await SaveAsync(f, ChartWidget(f), new { id, kind = "Text", text = "Goodbye **now**", textAlign = "right", textSize = "small" }));

        var text = Texts(second).Single();
        Assert.Equal(id, text.GetProperty("id").GetGuid());
        Assert.Equal("Goodbye **now**", text.GetProperty("text").GetString());
        Assert.Equal(("right", "small"), (text.GetProperty("textAlign").GetString(), text.GetProperty("textSize").GetString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_text_widget_needs_some_text(string? text)
    {
        var f = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(f, new { kind = "Text", text })).StatusCode);
    }

    [Fact]
    public async Task Too_much_text_or_an_unknown_alignment_or_size_is_refused()
    {
        var f = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(f, new { kind = "Text", text = new string('x', 4001) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(f, new { kind = "Text", text = "x", textAlign = "justify" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(f, new { kind = "Text", text = "x", textSize = "huge" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(f, new { kind = "Text", text = new string('x', 4000) })).StatusCode);
    }

    [Fact]
    public async Task A_text_widget_is_not_a_chart_in_the_list_and_does_not_run()
    {
        var f = await NewFixtureAsync();
        await Read(await SaveAsync(f, ChartWidget(f), new { kind = "Text", text = "Notes" }));

        var list = await Read(await f.Owner.GetAsync($"/api/organizations/{f.Org}/dashboards"));
        var summary = Assert.Single(list.EnumerateArray());
        Assert.Equal(1, summary.GetProperty("widgetCount").GetInt32());
        Assert.Equal([f.Chart], summary.GetProperty("chartIds").EnumerateArray().Select(c => c.GetGuid()));

        var run = await Read(await f.Owner.PostAsJsonAsync(Url(f, "/run"), new { filters = new { } }));
        Assert.Single(run.GetProperty("widgets").EnumerateArray());
    }

    [Fact]
    public async Task A_text_widget_carries_nothing_of_a_chart_or_a_picture_whatever_was_sent()
    {
        var f = await NewFixtureAsync();

        var saved = await Read(await SaveAsync(f, new
        {
            kind = "Text", text = "Notes", chartId = f.Chart, imageKey = "0123456789abcdef0123456789abcdef.png", imageFit = "cover", alt = "x", linkUrl = "https://example.com",
        }));

        var text = Texts(saved).Single();
        Assert.Equal(Guid.Empty, text.GetProperty("chartId").GetGuid());
        Assert.Equal(JsonValueKind.Null, text.GetProperty("imageKey").ValueKind);
        Assert.Equal(JsonValueKind.Null, text.GetProperty("alt").ValueKind);
    }

    [Fact]
    public async Task Text_widgets_and_a_picture_can_sit_with_charts_and_count_towards_24()
    {
        var f = await NewFixtureAsync();
        var widgets = new List<object> { ChartWidget(f) };
        for (var i = 0; i < 23; i++) widgets.Add(new { kind = "Text", text = $"Note {i}" });
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(f, [.. widgets])).StatusCode);

        widgets.Add(new { kind = "Text", text = "One too many" });

        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(f, [.. widgets])).StatusCode);
    }

    [Fact]
    public async Task A_visitor_with_a_link_sees_the_text()
    {
        var f = await NewFixtureAsync();
        await Read(await SaveAsync(f, ChartWidget(f), new { kind = "Text", text = "## Welcome", textAlign = "center", layout = new { x = 0, y = 5, w = 12, h = 2 } }));
        var link = await Read(await f.Owner.PostAsJsonAsync(Url(f, "/shares"), new { expiresInDays = 30 }));
        var token = link.GetProperty("token").GetString()!;

        var shared = await Read(await factory.CreateApiClient().GetAsync($"/api/shared/dashboards/{token}"));

        var text = shared.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("kind").GetString() == "Text");
        Assert.Equal("## Welcome", text.GetProperty("text").GetString());
        Assert.Equal("center", text.GetProperty("textAlign").GetString());
        Assert.Equal(1, shared.GetProperty("charts").GetArrayLength());
    }

    [Fact]
    public async Task Viewers_cannot_save_text_widgets()
    {
        var f = await NewFixtureAsync();
        var (viewer, email, _) = await NewUserAsync(factory);
        (await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/members", new { email, role = "Viewer" })).EnsureSuccessStatusCode();

        var response = await viewer.PutAsJsonAsync(Url(f), new
        {
            name = "Sales", definition = new { widgets = new object[] { new { kind = "Text", text = "Nope" } }, filters = Array.Empty<object>() },
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
