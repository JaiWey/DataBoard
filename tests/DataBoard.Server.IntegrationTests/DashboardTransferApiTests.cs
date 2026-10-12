using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Duplicating a dashboard.</summary>
[Collection(ApiCollection.Name)]
public class DashboardTransferApiTests(ApiFactory factory)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0];

    private sealed record Org(HttpClient Owner, Guid Id, Guid Dataset, Dictionary<string, Guid> Charts);

    private static string Dashboards(Guid org, string suffix = "") => $"/api/organizations/{org}/dashboards{suffix}";

    private async Task<Org> NewOrgAsync(params string[] chartNames)
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var source = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        source.EnsureSuccessStatusCode();
        var sourceId = (await source.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id;
        var dataset = (await Read(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/datasets",
            new { dataSourceId = sourceId, name = "Orders", sql = "SELECT id, region, price FROM orders" }))).GetProperty("id").GetGuid();
        var charts = new Dictionary<string, Guid>();
        foreach (var name in chartNames)
            charts[name] = (await Read(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/charts", new
            {
                name, datasetId = dataset, type = "Number",
                definition = new { query = new { measures = new[] { new { aggregation = "Sum", column = "price", alias = "n" } } } },
            }))).GetProperty("id").GetGuid();
        return new Org(owner, org.Id, dataset, charts);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static MultipartFormDataContent File(byte[] bytes, string name = "a.png")
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "File", name } };
        return form;
    }

    /// <summary>A dashboard that uses everything: two tabs, charts, text, a picture, a filter, a background and a folder.</summary>
    private async Task<(Guid Dashboard, Guid Folder)> BuildAsync(Org o)
    {
        var folder = (await Read(await o.Owner.PostAsJsonAsync($"/api/organizations/{o.Id}/folders", new { name = "Sales", kind = "Dashboard" }))).GetProperty("id").GetGuid();
        var made = await Read(await o.Owner.PostAsJsonAsync(Dashboards(o.Id), new
        {
            name = "Sales", description = "Weekly", folderId = folder,
            definition = new
            {
                widgets = new object[] { new { chartId = o.Charts["Revenue"], layout = new { x = 0, y = 0, w = 6, h = 5 } } },
                filters = new object[] { new { label = "Region", column = "region", @operator = "In" } },
            },
        }));
        var id = made.GetProperty("id").GetGuid();

        // Two tabs, the chart on the second; then a text widget and a picture on the first.
        var withTabs = await Read(await o.Owner.PutAsJsonAsync(Dashboards(o.Id, $"/{id}"), new
        {
            name = "Sales", description = "Weekly",
            definition = new
            {
                widgets = new object[] { new { id = made.GetProperty("definition").GetProperty("widgets")[0].GetProperty("id").GetGuid(), chartId = o.Charts["Revenue"], layout = new { x = 0, y = 0, w = 6, h = 5 } } },
                filters = made.GetProperty("definition").GetProperty("filters").EnumerateArray().Select(f => (object)new { id = f.GetProperty("id").GetGuid(), label = "Region", column = "region", @operator = "In" }).ToArray(),
                tabs = new object[] { new { name = "Overview" }, new { name = "Costs" } },
            },
        }));
        var tabs = withTabs.GetProperty("definition").GetProperty("tabs").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToArray();
        var chartWidget = withTabs.GetProperty("definition").GetProperty("widgets")[0].GetProperty("id").GetGuid();
        await Read(await o.Owner.PutAsJsonAsync(Dashboards(o.Id, $"/{id}"), new
        {
            name = "Sales", description = "Weekly",
            definition = new
            {
                widgets = new object[]
                {
                    new { id = chartWidget, chartId = o.Charts["Revenue"], tabId = tabs[1], layout = new { x = 0, y = 0, w = 6, h = 5 } },
                    new { kind = "Text", text = "# Notes\nSee **below**", textAlign = "center", textSize = "large", tabId = tabs[0], layout = new { x = 0, y = 0, w = 12, h = 2 } },
                },
                filters = withTabs.GetProperty("definition").GetProperty("filters").EnumerateArray().Select(f => (object)new { id = f.GetProperty("id").GetGuid(), label = "Region", column = "region", @operator = "In" }).ToArray(),
                tabs = tabs.Select((t, i) => (object)new { id = t, name = i == 0 ? "Overview" : "Costs" }).ToArray(),
            },
        }));
        var picture = MultipartWithTab(Png, tabs[0], "Our logo");
        await Read(await o.Owner.PostAsync(Dashboards(o.Id, $"/{id}/image-widgets"), picture));
        await Read(await o.Owner.PutAsync(Dashboards(o.Id, $"/{id}/background"), BackgroundForm(Jpeg, "tile", 40)));
        return (id, folder);
    }

    private static MultipartFormDataContent MultipartWithTab(byte[] bytes, Guid tab, string alt)
    {
        var form = File(bytes);
        form.Add(new StringContent(tab.ToString()), "TabId");
        form.Add(new StringContent(alt), "Alt");
        form.Add(new StringContent("https://example.com"), "LinkUrl");
        form.Add(new StringContent("cover"), "Fit");
        return form;
    }

    private static MultipartFormDataContent BackgroundForm(byte[] bytes, string fit, int opacity)
    {
        var form = File(bytes, "bg.jpg");
        form.Add(new StringContent(fit), "Fit");
        form.Add(new StringContent(opacity.ToString()), "Opacity");
        return form;
    }

    private int PictureFiles() =>
        Directory.Exists(Path.Combine(factory.UploadFolder, "images")) ? Directory.GetFiles(Path.Combine(factory.UploadFolder, "images")).Length : 0;

    private static JsonElement[] Widgets(JsonElement dashboard) => dashboard.GetProperty("definition").GetProperty("widgets").EnumerateArray().ToArray();

    // ---- duplicate -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_copy_has_everything_the_original_has_with_new_ids_and_its_own_pictures()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, folder) = await BuildAsync(o);
        var original = await Read(await o.Owner.GetAsync(Dashboards(o.Id, $"/{id}")));

        var copy = await Read(await o.Owner.PostAsJsonAsync(Dashboards(o.Id, $"/{id}/duplicate"), new { }));

        Assert.NotEqual(id, copy.GetProperty("id").GetGuid());
        Assert.Equal("Copy of Sales", copy.GetProperty("name").GetString());
        Assert.Equal("Weekly", copy.GetProperty("description").GetString());
        Assert.Equal(folder, copy.GetProperty("folderId").GetGuid());
        Assert.Equal(["Overview", "Costs"], copy.GetProperty("definition").GetProperty("tabs").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        var oldTabs = original.GetProperty("definition").GetProperty("tabs").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToHashSet();
        var newTabs = copy.GetProperty("definition").GetProperty("tabs").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToArray();
        Assert.DoesNotContain(newTabs, oldTabs.Contains);

        var kinds = Widgets(copy).Select(w => w.GetProperty("kind").GetString()).Order().ToArray();
        Assert.Equal(["Chart", "Image", "Text"], kinds);
        var oldIds = Widgets(original).Select(w => w.GetProperty("id").GetGuid()).ToHashSet();
        Assert.All(Widgets(copy), w =>
        {
            Assert.DoesNotContain(w.GetProperty("id").GetGuid(), oldIds);
            Assert.Contains(w.GetProperty("tabId").GetGuid(), newTabs);
        });
        Assert.Equal(o.Charts["Revenue"], Widgets(copy).Single(w => w.GetProperty("kind").GetString() == "Chart").GetProperty("chartId").GetGuid()); // charts stay shared
        var oldFilter = original.GetProperty("definition").GetProperty("filters")[0].GetProperty("id").GetGuid();
        Assert.NotEqual(oldFilter, copy.GetProperty("definition").GetProperty("filters")[0].GetProperty("id").GetGuid());
        Assert.Equal("tile", copy.GetProperty("background").GetProperty("fit").GetString());
        Assert.Equal(40, copy.GetProperty("background").GetProperty("opacity").GetInt32());
    }

    [Fact]
    public async Task A_copy_keeps_its_own_picture_files_so_deleting_one_dashboard_leaves_the_other_whole()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);
        var before = PictureFiles();
        var copy = await Read(await o.Owner.PostAsJsonAsync(Dashboards(o.Id, $"/{id}/duplicate"), new { }));
        var copyId = copy.GetProperty("id").GetGuid();
        Assert.Equal(before + 2, PictureFiles()); // the picture and the background

        (await o.Owner.DeleteAsync(Dashboards(o.Id, $"/{id}"))).EnsureSuccessStatusCode();

        var picture = Widgets(copy).Single(w => w.GetProperty("kind").GetString() == "Image");
        var served = await o.Owner.GetAsync(Dashboards(o.Id, $"/{copyId}/image-widgets/{picture.GetProperty("id").GetGuid()}/image"));
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync());
        Assert.Equal(Jpeg, await (await o.Owner.GetAsync(Dashboards(o.Id, $"/{copyId}/background"))).Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Copies_get_their_own_names_and_a_name_can_be_chosen()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);

        var first = await Read(await o.Owner.PostAsJsonAsync(Dashboards(o.Id, $"/{id}/duplicate"), new { }));
        var second = await Read(await o.Owner.PostAsJsonAsync(Dashboards(o.Id, $"/{id}/duplicate"), new { }));
        var named = await Read(await o.Owner.PostAsJsonAsync(Dashboards(o.Id, $"/{id}/duplicate"), new { name = "Quarterly" }));
        var taken = await Read(await o.Owner.PostAsJsonAsync(Dashboards(o.Id, $"/{id}/duplicate"), new { name = "quarterly" }));

        Assert.Equal(["Copy of Sales", "Copy of Sales (2)", "Quarterly", "quarterly (2)"],
            new[] { first, second, named, taken }.Select(c => c.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_copy_has_no_share_links_and_no_refresh_schedule()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);
        (await o.Owner.PostAsJsonAsync(Dashboards(o.Id, $"/{id}/shares"), new { expiresInDays = 30 })).EnsureSuccessStatusCode();
        (await o.Owner.PutAsJsonAsync(Dashboards(o.Id, $"/{id}/refresh"), new { intervalMinutes = 60 })).EnsureSuccessStatusCode();

        var copy = await Read(await o.Owner.PostAsJsonAsync(Dashboards(o.Id, $"/{id}/duplicate"), new { }));

        Assert.Equal(JsonValueKind.Null, copy.GetProperty("refresh").GetProperty("intervalMinutes").ValueKind);
        var shares = await Read(await o.Owner.GetAsync(Dashboards(o.Id, $"/{copy.GetProperty("id").GetGuid()}/shares")));
        Assert.Equal(0, shares.GetArrayLength());
    }

    [Fact]
    public async Task A_dashboard_without_tabs_or_pictures_is_copied_too()
    {
        var o = await NewOrgAsync("Revenue");
        var plain = await Read(await o.Owner.PostAsJsonAsync(Dashboards(o.Id), new
        {
            name = "Plain", definition = new { widgets = new object[] { new { chartId = o.Charts["Revenue"] } }, filters = Array.Empty<object>() },
        }));

        var copy = await Read(await o.Owner.PostAsJsonAsync(Dashboards(o.Id, $"/{plain.GetProperty("id").GetGuid()}/duplicate"), new { }));

        Assert.Single(Widgets(copy));
        Assert.Equal(JsonValueKind.Null, copy.GetProperty("background").ValueKind);
        Assert.False(copy.GetProperty("definition").TryGetProperty("tabs", out var tabs) && tabs.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task Only_editors_can_copy_and_other_organizations_cannot()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);
        var (viewer, email, _) = await NewUserAsync(factory);
        (await o.Owner.PostAsJsonAsync($"/api/organizations/{o.Id}/members", new { email, role = "Viewer" })).EnsureSuccessStatusCode();
        var (stranger, _, _) = await NewUserAsync(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(Dashboards(o.Id, $"/{id}/duplicate"), new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(Dashboards(o.Id, $"/{id}/duplicate"), new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await o.Owner.PostAsJsonAsync(Dashboards(o.Id, $"/{Guid.NewGuid()}/duplicate"), new { })).StatusCode);
    }
}
