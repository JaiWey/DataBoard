using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Duplicating a dashboard, and taking one out as a file and bringing it back, in this organization or another.</summary>
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

    // ---- export ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_export_names_charts_and_tabs_and_carries_the_pictures_without_any_id_or_query()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);

        var json = await o.Owner.GetStringAsync(Dashboards(o.Id, $"/{id}/export"));
        var file = JsonDocument.Parse(json).RootElement;

        Assert.Equal("databoard-dashboard", file.GetProperty("format").GetString());
        Assert.Equal(1, file.GetProperty("version").GetInt32());
        Assert.Equal("Sales", file.GetProperty("name").GetString());
        Assert.Equal(["tab-1", "tab-2"], file.GetProperty("tabs").EnumerateArray().Select(t => t.GetProperty("key").GetString()));
        var widgets = file.GetProperty("widgets").EnumerateArray().ToArray();
        var chart = widgets.Single(w => w.GetProperty("kind").GetString() == "Chart");
        Assert.Equal("Revenue", chart.GetProperty("chart").GetString());
        Assert.Equal("tab-2", chart.GetProperty("tab").GetString());
        var picture = widgets.Single(w => w.GetProperty("kind").GetString() == "Image");
        Assert.Equal("Our logo", picture.GetProperty("alt").GetString());
        Assert.Equal(Png, Convert.FromBase64String(file.GetProperty("files").GetProperty(picture.GetProperty("image").GetString()!).GetProperty("data").GetString()!));
        Assert.Equal("tile", file.GetProperty("background").GetProperty("fit").GetString());
        // Nothing that points inside this organization: no guid, no SQL, no dataset.
        Assert.DoesNotContain(o.Charts["Revenue"].ToString(), json);
        Assert.DoesNotContain(o.Dataset.ToString(), json);
        Assert.DoesNotContain(id.ToString(), json);
        Assert.DoesNotContain("SELECT", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_picture_used_twice_is_in_the_file_once_and_a_deleted_chart_is_left_out()
    {
        var o = await NewOrgAsync("Revenue", "Average");
        var made = await Read(await o.Owner.PostAsJsonAsync(Dashboards(o.Id), new
        {
            name = "Two", definition = new { widgets = new object[] { new { chartId = o.Charts["Revenue"] }, new { chartId = o.Charts["Average"] } }, filters = Array.Empty<object>() },
        }));
        var id = made.GetProperty("id").GetGuid();
        await Read(await o.Owner.PutAsync(Dashboards(o.Id, $"/{id}/background"), BackgroundForm(Png, "cover", 100)));
        (await o.Owner.DeleteAsync($"/api/organizations/{o.Id}/charts/{o.Charts["Average"]}")).EnsureSuccessStatusCode();

        var file = await Read(await o.Owner.GetAsync(Dashboards(o.Id, $"/{id}/export")));

        Assert.Equal(["Revenue"], file.GetProperty("widgets").EnumerateArray().Select(w => w.GetProperty("chart").GetString()));
    }

    [Fact]
    public async Task Only_editors_can_export()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);
        var (viewer, email, _) = await NewUserAsync(factory);
        (await o.Owner.PostAsJsonAsync($"/api/organizations/{o.Id}/members", new { email, role = "Viewer" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(Dashboards(o.Id, $"/{id}/export"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await o.Owner.GetAsync(Dashboards(o.Id, $"/{Guid.NewGuid()}/export"))).StatusCode);
    }

    // ---- import ----------------------------------------------------------------------------------------------

    private static async Task<JsonNode> ExportAsync(Org o, Guid id) =>
        JsonNode.Parse(await o.Owner.GetStringAsync(Dashboards(o.Id, $"/{id}/export")))!;

    private static Task<HttpResponseMessage> ImportAsync(Org into, JsonNode? package, string? name = null, Guid? folderId = null) =>
        into.Owner.PostAsJsonAsync(Dashboards(into.Id, "/import"), new { package, name, folderId });

    [Fact]
    public async Task A_file_brought_back_into_the_same_organization_makes_a_new_dashboard_just_like_it()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);
        var package = await ExportAsync(o, id);

        var result = await Read(await ImportAsync(o, package));

        var dashboard = result.GetProperty("dashboard");
        Assert.Equal("Sales (2)", dashboard.GetProperty("name").GetString());
        Assert.Equal(0, result.GetProperty("missingCharts").GetArrayLength());
        Assert.Equal("Weekly", dashboard.GetProperty("description").GetString());
        Assert.Equal(["Overview", "Costs"], dashboard.GetProperty("definition").GetProperty("tabs").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(["Chart", "Image", "Text"], Widgets(dashboard).Select(w => w.GetProperty("kind").GetString()).Order());
        Assert.Equal("Region", dashboard.GetProperty("definition").GetProperty("filters")[0].GetProperty("label").GetString());
        var text = Widgets(dashboard).Single(w => w.GetProperty("kind").GetString() == "Text");
        Assert.Equal(("center", "large"), (text.GetProperty("textAlign").GetString(), text.GetProperty("textSize").GetString()));
        var picture = Widgets(dashboard).Single(w => w.GetProperty("kind").GetString() == "Image");
        var newId = dashboard.GetProperty("id").GetGuid();
        Assert.Equal(Png, await (await o.Owner.GetAsync(Dashboards(o.Id, $"/{newId}/image-widgets/{picture.GetProperty("id").GetGuid()}/image"))).Content.ReadAsByteArrayAsync());
        Assert.Equal(Jpeg, await (await o.Owner.GetAsync(Dashboards(o.Id, $"/{newId}/background"))).Content.ReadAsByteArrayAsync());
        Assert.Equal("tile", dashboard.GetProperty("background").GetProperty("fit").GetString());
        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("folderId").ValueKind); // a file does not say where to file it
    }

    [Fact]
    public async Task A_file_in_another_organization_finds_charts_by_name_and_reports_the_ones_it_lacks()
    {
        var from = await NewOrgAsync("Revenue", "Average");
        var made = await Read(await from.Owner.PostAsJsonAsync(Dashboards(from.Id), new
        {
            name = "Pair", definition = new
            {
                widgets = new object[] { new { chartId = from.Charts["Revenue"] }, new { chartId = from.Charts["Average"] }, new { kind = "Text", text = "Hello" } },
                filters = Array.Empty<object>(),
            },
        }));
        var package = await ExportAsync(from, made.GetProperty("id").GetGuid());
        var into = await NewOrgAsync("revenue"); // the name matches whatever the case

        var result = await Read(await ImportAsync(into, package));

        Assert.Equal(["Average"], result.GetProperty("missingCharts").EnumerateArray().Select(c => c.GetString()));
        var widgets = Widgets(result.GetProperty("dashboard"));
        Assert.Equal(2, widgets.Length);
        Assert.Equal(into.Charts["revenue"], widgets.Single(w => w.GetProperty("kind").GetString() == "Chart").GetProperty("chartId").GetGuid());
    }

    [Fact]
    public async Task A_name_can_be_chosen_and_the_dashboard_filed_in_a_folder()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, folder) = await BuildAsync(o);
        var package = await ExportAsync(o, id);

        var result = await Read(await ImportAsync(o, package, "From file", folder));

        var dashboard = result.GetProperty("dashboard");
        Assert.Equal("From file", dashboard.GetProperty("name").GetString());
        Assert.Equal(folder, dashboard.GetProperty("folderId").GetGuid());
    }

    [Fact]
    public async Task A_folder_that_is_not_a_dashboard_folder_is_refused()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);
        var package = await ExportAsync(o, id);
        var chartFolder = (await Read(await o.Owner.PostAsJsonAsync($"/api/organizations/{o.Id}/folders", new { name = "Charts", kind = "Chart" }))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(o, package, null, chartFolder)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(o, package, null, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task What_is_not_a_dashboard_file_is_refused()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);
        var good = await ExportAsync(o, id);
        JsonNode Changed(Action<JsonObject> change)
        {
            var copy = JsonNode.Parse(good.ToJsonString())!.AsObject();
            change(copy);
            return copy;
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(o, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(o, Changed(p => p["format"] = "something-else"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(o, Changed(p => p["version"] = 2))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(o, Changed(p => p["name"] = "  "), "")).StatusCode);
    }

    [Fact]
    public async Task A_bad_picture_or_a_picture_that_is_missing_refuses_the_whole_file_and_keeps_nothing()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);
        var good = await ExportAsync(o, id);
        var before = PictureFiles();
        JsonNode Changed(Action<JsonObject> change)
        {
            var copy = JsonNode.Parse(good.ToJsonString())!.AsObject();
            change(copy);
            return copy;
        }
        string FirstFile(JsonObject p) => p["files"]!.AsObject().First().Key;

        var svg = Changed(p => p["files"]![FirstFile(p)]!["data"] = Convert.ToBase64String("<svg><script>1</script></svg>"u8.ToArray()));
        var notBase64 = Changed(p => p["files"]![FirstFile(p)]!["data"] = "!!! not base64 !!!");
        var missing = Changed(p => p["files"] = new JsonObject());
        var empty = Changed(p => p["files"]![FirstFile(p)]!["data"] = "");

        foreach (var broken in new[] { svg, notBase64, missing, empty })
            Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(o, broken)).StatusCode);
        Assert.Equal(before, PictureFiles()); // no picture of a refused file is left behind
    }

    [Fact]
    public async Task A_picture_over_the_limit_inside_a_file_is_refused()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);
        var good = await ExportAsync(o, id);
        var before = PictureFiles();
        var big = new byte[5 * 1024 * 1024 + 10];
        Png.CopyTo(big, 0);
        var copy = JsonNode.Parse(good.ToJsonString())!.AsObject();
        var key = copy["files"]!.AsObject().First().Key;
        copy["files"]![key]!["data"] = Convert.ToBase64String(big);

        var response = await ImportAsync(o, copy);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, PictureFiles());
    }

    [Fact]
    public async Task Widgets_and_filters_in_a_file_are_held_to_the_rules_of_any_dashboard()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);
        var good = await ExportAsync(o, id);
        JsonNode Changed(Action<JsonObject> change)
        {
            var copy = JsonNode.Parse(good.ToJsonString())!.AsObject();
            change(copy);
            return copy;
        }
        JsonObject Widget(JsonObject p, string kind) => p["widgets"]!.AsArray().Select(w => w!.AsObject()).First(w => w["kind"]!.GetValue<string>() == kind);

        var cases = new Dictionary<string, JsonNode>
        {
            ["link"] = Changed(p => Widget(p, "Image")["linkUrl"] = "javascript:alert(1)"),
            ["fit"] = Changed(p => Widget(p, "Image")["imageFit"] = "stretch"),
            ["long text"] = Changed(p => Widget(p, "Text")["text"] = new string('x', 4001)),
            ["no text"] = Changed(p => Widget(p, "Text")["text"] = " "),
            ["align"] = Changed(p => Widget(p, "Text")["textAlign"] = "justify"),
            ["tab not in file"] = Changed(p => Widget(p, "Text")["tab"] = "tab-9"),
            ["layout"] = Changed(p => Widget(p, "Text")["layout"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = 40, ["h"] = 2 }),
            ["tab name"] = Changed(p => p["tabs"]![0]!["name"] = ""),
            ["same tab key twice"] = Changed(p => p["tabs"]![1]!["key"] = "tab-1"),
            ["background file"] = Changed(p => p["background"]!["image"] = "nope"),
            ["background fit"] = Changed(p => p["background"]!["fit"] = "stretch"),
            ["background strength"] = Changed(p => p["background"]!["opacity"] = 5),
            ["filter"] = Changed(p => p["filters"]![0]!["label"] = ""),
            ["unknown kind"] = Changed(p => Widget(p, "Text")["kind"] = 7),
        };
        var before = PictureFiles();

        foreach (var (name, package) in cases)
            Assert.True((await ImportAsync(o, package)).StatusCode == HttpStatusCode.BadRequest, $"{name} should be refused");
        Assert.Equal(before, PictureFiles());
    }

    [Fact]
    public async Task A_file_with_more_than_24_widgets_or_ten_tabs_is_refused()
    {
        var o = await NewOrgAsync("Revenue");
        var package = new JsonObject
        {
            ["format"] = "databoard-dashboard", ["version"] = 1, ["name"] = "Big",
            ["widgets"] = new JsonArray(Enumerable.Range(0, 25).Select(i => (JsonNode)new JsonObject { ["kind"] = "Text", ["text"] = $"n{i}" }).ToArray()),
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(o, package)).StatusCode);

        package["widgets"] = new JsonArray();
        package["tabs"] = new JsonArray(Enumerable.Range(0, 11).Select(i => (JsonNode)new JsonObject { ["key"] = $"t{i}", ["name"] = $"Tab {i}" }).ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(o, package)).StatusCode);
    }

    [Fact]
    public async Task A_small_hand_written_file_works()
    {
        var o = await NewOrgAsync("Revenue");
        var package = JsonNode.Parse("""
            { "format": "databoard-dashboard", "version": 1, "name": "By hand",
              "widgets": [ { "kind": "Chart", "chart": "Revenue" }, { "kind": "Text", "text": "Hi" } ] }
            """)!;

        var result = await Read(await ImportAsync(o, package));

        Assert.Equal(2, Widgets(result.GetProperty("dashboard")).Length);
    }

    [Fact]
    public async Task Only_editors_can_import()
    {
        var o = await NewOrgAsync("Revenue");
        var (id, _) = await BuildAsync(o);
        var package = await ExportAsync(o, id);
        var (viewer, email, _) = await NewUserAsync(factory);
        (await o.Owner.PostAsJsonAsync($"/api/organizations/{o.Id}/members", new { email, role = "Viewer" })).EnsureSuccessStatusCode();
        var (stranger, _, _) = await NewUserAsync(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(Dashboards(o.Id, "/import"), new { package })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(Dashboards(o.Id, "/import"), new { package })).StatusCode);
    }
}
