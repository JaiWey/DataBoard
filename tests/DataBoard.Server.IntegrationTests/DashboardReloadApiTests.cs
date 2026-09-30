using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>How often an open dashboard (and its public page) asks for its charts again. Saved with the dashboard.</summary>
[Collection(ApiCollection.Name)]
public class DashboardReloadApiTests(ApiFactory factory)
{
    private sealed record Fixture(HttpClient Owner, Guid Org, Guid Dashboard);

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
            definition = new { query = new { measures = new[] { new { aggregation = "Sum", column = "price", alias = "n" } } } },
        }));
        var dashboard = await Read(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/dashboards", new
        {
            name = "Sales",
            definition = new
            {
                widgets = new object[] { new { chartId = chart.GetProperty("id").GetGuid(), layout = new { x = 0, y = 0, w = 6, h = 5 } } },
                filters = Array.Empty<object>(),
            },
        }));
        return new Fixture(owner, org.Id, dashboard.GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>Saves the dashboard again with another reload interval, keeping everything else as it is.</summary>
    private static async Task<HttpResponseMessage> SetReloadAsync(Fixture f, int? seconds)
    {
        var current = JsonNode.Parse(await f.Owner.GetStringAsync(Url(f)))!;
        var definition = current["definition"]!.AsObject();
        definition["reloadSeconds"] = seconds;
        return await f.Owner.PutAsJsonAsync(Url(f), new { name = current["name"]!.GetValue<string>(), definition });
    }

    private static int? ReloadOf(JsonElement dashboard) =>
        dashboard.GetProperty("definition").TryGetProperty("reloadSeconds", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    [Fact]
    public async Task A_dashboard_does_not_reload_until_it_is_asked_to()
    {
        var f = await NewFixtureAsync();

        Assert.Null(ReloadOf(await Read(await f.Owner.GetAsync(Url(f)))));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(3600)]
    public async Task An_interval_is_saved_with_the_dashboard_and_can_be_cleared(int seconds)
    {
        var f = await NewFixtureAsync();

        Assert.Equal(seconds, ReloadOf(await Read(await SetReloadAsync(f, seconds))));
        Assert.Equal(seconds, ReloadOf(await Read(await f.Owner.GetAsync(Url(f)))));

        Assert.Null(ReloadOf(await Read(await SetReloadAsync(f, null))));
        Assert.Null(ReloadOf(await Read(await f.Owner.GetAsync(Url(f)))));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3601)]
    public async Task An_interval_outside_ten_seconds_to_an_hour_is_refused_and_nothing_changes(int seconds)
    {
        var f = await NewFixtureAsync();
        await SetReloadAsync(f, 60);

        var response = await SetReloadAsync(f, seconds);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("reloads every 10 to 3600 seconds", await response.Content.ReadAsStringAsync());
        Assert.Equal(60, ReloadOf(await Read(await f.Owner.GetAsync(Url(f)))));
    }

    [Fact]
    public async Task Saving_a_layout_change_does_not_lose_the_interval()
    {
        var f = await NewFixtureAsync();
        await SetReloadAsync(f, 30);

        var current = JsonNode.Parse(await f.Owner.GetStringAsync(Url(f)))!;
        current["definition"]!["widgets"]![0]!["layout"]!["y"] = 3;
        var saved = await Read(await f.Owner.PutAsJsonAsync(Url(f), new { name = "Sales", definition = current["definition"] }));

        Assert.Equal(30, ReloadOf(saved));
    }

    [Fact]
    public async Task A_copy_and_an_exported_file_keep_the_interval()
    {
        var f = await NewFixtureAsync();
        await SetReloadAsync(f, 300);

        var copy = await Read(await f.Owner.PostAsJsonAsync(Url(f, "/duplicate"), new { }));
        Assert.Equal(300, ReloadOf(copy));

        var package = JsonNode.Parse(await f.Owner.GetStringAsync(Url(f, "/export")))!;
        Assert.Equal(300, package["reloadSeconds"]!.GetValue<int>());

        var imported = await Read(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/dashboards/import", new { package }));
        Assert.Equal(300, ReloadOf(imported.GetProperty("dashboard")));
    }

    [Fact]
    public async Task A_file_with_an_interval_out_of_range_is_refused()
    {
        var f = await NewFixtureAsync();
        var package = JsonNode.Parse(await f.Owner.GetStringAsync(Url(f, "/export")))!;
        package["reloadSeconds"] = 5;

        var response = await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/dashboards/import", new { package });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_public_page_gets_the_interval()
    {
        var f = await NewFixtureAsync();
        var token = (await Read(await f.Owner.PostAsJsonAsync(Url(f, "/shares"), new { expiresInDays = 30 }))).GetProperty("token").GetString();
        var visitor = factory.CreateApiClient();

        var without = await Read(await visitor.GetAsync($"/api/shared/dashboards/{token}"));
        Assert.True(!without.TryGetProperty("reloadSeconds", out var none) || none.ValueKind == JsonValueKind.Null);

        await SetReloadAsync(f, 60);
        var with = await Read(await visitor.GetAsync($"/api/shared/dashboards/{token}"));
        Assert.Equal(60, with.GetProperty("reloadSeconds").GetInt32());
    }
}
