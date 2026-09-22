using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Pictures as widgets on the grid, next to charts: adding, showing, changing, replacing, removing and sharing them.</summary>
[Collection(ApiCollection.Name)]
public class DashboardImageWidgetApiTests(ApiFactory factory)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0];
    private static readonly byte[] Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

    private sealed record Fixture(HttpClient Owner, Guid Org, Guid Dashboard, Guid Chart);

    private static string Url(Fixture f, string suffix = "") => $"/api/organizations/{f.Org}/dashboards/{f.Dashboard}{suffix}";

    /// <summary>A dashboard with one chart on it, in the top left (rows 0 to 4).</summary>
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

    private static MultipartFormDataContent Form(byte[]? image, string fileName = "logo.png", string? fit = null, string? alt = null, string? link = null)
    {
        var form = new MultipartFormDataContent();
        if (image is not null)
        {
            var file = new ByteArrayContent(image);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png"); // what a browser says; never trusted
            form.Add(file, "File", fileName);
        }
        if (fit is not null) form.Add(new StringContent(fit), "Fit");
        if (alt is not null) form.Add(new StringContent(alt), "Alt");
        if (link is not null) form.Add(new StringContent(link), "LinkUrl");
        return form;
    }

    private static JsonElement[] Images(JsonElement dashboard) =>
        dashboard.GetProperty("definition").GetProperty("widgets").EnumerateArray().Where(w => w.GetProperty("kind").GetString() == "Image").ToArray();

    private static string ImageUrl(Fixture f, JsonElement widget) =>
        Url(f, $"/image-widgets/{widget.GetProperty("id").GetGuid()}/image");

    private async Task<HttpClient> AddMemberAsync(Fixture f, string role)
    {
        var (member, email, _) = await NewUserAsync(factory);
        (await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/members", new { email, role })).EnsureSuccessStatusCode();
        return member;
    }

    /// <summary>Saves the dashboard with its widgets as they are now (the way the client does), changing some of them.</summary>
    private static async Task<HttpResponseMessage> SaveAsync(Fixture f, JsonElement dashboard, Func<JsonElement, object> change)
    {
        var widgets = dashboard.GetProperty("definition").GetProperty("widgets").EnumerateArray().Select(change).ToArray();
        return await f.Owner.PutAsJsonAsync(Url(f), new { name = dashboard.GetProperty("name").GetString(), definition = new { widgets, filters = Array.Empty<object>() } });
    }

    [Fact]
    public async Task A_picture_is_added_as_a_widget_below_the_others_and_comes_back_with_its_type()
    {
        var f = await NewFixtureAsync();

        var dashboard = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png, alt: "Company logo", link: "https://example.com")));

        var widget = Assert.Single(Images(dashboard));
        Assert.Equal("contain", widget.GetProperty("imageFit").GetString());
        Assert.Equal("Company logo", widget.GetProperty("alt").GetString());
        Assert.Equal("https://example.com", widget.GetProperty("linkUrl").GetString());
        var layout = widget.GetProperty("layout");
        Assert.Equal((0, 5, 4, 4), (layout.GetProperty("x").GetInt32(), layout.GetProperty("y").GetInt32(), layout.GetProperty("w").GetInt32(), layout.GetProperty("h").GetInt32()));

        var picture = await f.Owner.GetAsync(ImageUrl(f, widget));
        Assert.Equal("image/png", picture.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await picture.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", picture.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(2, dashboard.GetProperty("definition").GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task Pictures_are_not_charts_in_the_list_and_do_not_run()
    {
        var f = await NewFixtureAsync();
        await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png)));

        var list = await Read(await f.Owner.GetAsync($"/api/organizations/{f.Org}/dashboards"));
        var summary = Assert.Single(list.EnumerateArray());
        Assert.Equal(1, summary.GetProperty("widgetCount").GetInt32());
        Assert.Equal([f.Chart], summary.GetProperty("chartIds").EnumerateArray().Select(c => c.GetGuid()));

        var run = await Read(await f.Owner.PostAsJsonAsync(Url(f, "/run"), new { filters = new { } }));
        Assert.Single(run.GetProperty("widgets").EnumerateArray()); // only the chart has a result
    }

    [Fact]
    public async Task How_a_picture_is_shown_can_be_changed_with_the_dashboard_but_not_where_it_is_kept()
    {
        var f = await NewFixtureAsync();
        var dashboard = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png)));
        var key = Images(dashboard).Single().GetProperty("imageKey").GetString();

        var saved = await Read(await SaveAsync(f, dashboard, w => w.GetProperty("kind").GetString() == "Image"
            ? new
            {
                id = w.GetProperty("id").GetGuid(), chartId = Guid.Empty, layout = new { x = 6, y = 0, w = 6, h = 3 }, kind = "Image",
                imageKey = "00000000000000000000000000000000.png", // a client cannot choose the file
                imageFit = "cover", alt = "  Our logo ", linkUrl = "https://example.com/about",
            }
            : new { id = w.GetProperty("id").GetGuid(), chartId = f.Chart, layout = new { x = 0, y = 0, w = 6, h = 5 }, kind = "Chart", imageKey = (string?)null, imageFit = (string?)null, alt = (string?)null, linkUrl = (string?)null }));

        var widget = Assert.Single(Images(saved));
        Assert.Equal(key, widget.GetProperty("imageKey").GetString());
        Assert.Equal("cover", widget.GetProperty("imageFit").GetString());
        Assert.Equal("Our logo", widget.GetProperty("alt").GetString());
        Assert.Equal(6, widget.GetProperty("layout").GetProperty("x").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await f.Owner.GetAsync(ImageUrl(f, widget))).StatusCode);
    }

    [Theory]
    [InlineData("stretch", null, null)]
    [InlineData(null, null, "javascript:alert(1)")]
    [InlineData(null, null, "ftp://example.com/x")]
    [InlineData(null, null, "not a url")]
    [InlineData(null, "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", null)]
    public async Task A_fit_a_description_or_a_link_that_is_not_allowed_is_refused_when_adding_and_when_saving(string? fit, string? alt, string? link)
    {
        var f = await NewFixtureAsync();

        var adding = await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png, fit: fit, alt: alt, link: link));
        Assert.Equal(HttpStatusCode.BadRequest, adding.StatusCode);

        var dashboard = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png)));
        var saving = await SaveAsync(f, dashboard, w => w.GetProperty("kind").GetString() == "Image"
            ? new { id = w.GetProperty("id").GetGuid(), chartId = Guid.Empty, kind = "Image", imageFit = fit, alt, linkUrl = link }
            : new { id = w.GetProperty("id").GetGuid(), chartId = f.Chart, kind = "Chart", imageFit = (string?)null, alt = (string?)null, linkUrl = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, saving.StatusCode);
    }

    [Fact]
    public async Task A_picture_widget_cannot_be_made_by_saving_the_dashboard()
    {
        var f = await NewFixtureAsync();

        var response = await f.Owner.PutAsJsonAsync(Url(f), new
        {
            name = "Sales",
            definition = new
            {
                widgets = new object[] { new { id = Guid.NewGuid(), chartId = Guid.Empty, kind = "Image", imageKey = "0123456789abcdef0123456789abcdef.png" } },
                filters = Array.Empty<object>(),
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task What_is_not_a_picture_is_refused_whatever_it_is_called_and_nothing_is_added()
    {
        var f = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Svg, "logo.svg"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Svg, "logo.png"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(null))).StatusCode);

        var dashboard = await Read(await f.Owner.GetAsync(Url(f)));
        Assert.Empty(Images(dashboard));
    }

    [Fact]
    public async Task A_picture_can_be_replaced_and_keeps_its_place_and_settings()
    {
        var f = await NewFixtureAsync();
        var added = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png, fit: "cover", alt: "Logo")));
        var widget = Images(added).Single();
        var id = widget.GetProperty("id").GetGuid();

        var replaced = await Read(await f.Owner.PutAsync(Url(f, $"/image-widgets/{id}/image"), Form(Jpeg, "photo.jpg")));

        var after = Images(replaced).Single();
        Assert.NotEqual(widget.GetProperty("imageKey").GetString(), after.GetProperty("imageKey").GetString());
        Assert.Equal("cover", after.GetProperty("imageFit").GetString());
        Assert.Equal("Logo", after.GetProperty("alt").GetString());
        Assert.Equal(widget.GetProperty("layout").GetProperty("y").GetInt32(), after.GetProperty("layout").GetProperty("y").GetInt32());
        Assert.Equal("image/jpeg", (await f.Owner.GetAsync(ImageUrl(f, after))).Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.PutAsync(Url(f, $"/image-widgets/{Guid.NewGuid()}/image"), Form(Png))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.PutAsync(Url(f, $"/image-widgets/{f.Chart}/image"), Form(Png))).StatusCode); // not a picture
    }

    [Fact]
    public async Task Taking_the_widget_off_the_dashboard_removes_its_picture()
    {
        var f = await NewFixtureAsync();
        var added = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png)));
        var imageUrl = ImageUrl(f, Images(added).Single());
        Assert.Equal(HttpStatusCode.OK, (await f.Owner.GetAsync(imageUrl)).StatusCode);

        var saved = await Read(await RemoveImagesAsync(f, added));

        Assert.Empty(Images(saved));
        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.GetAsync(imageUrl)).StatusCode);
    }

    private async Task<HttpResponseMessage> RemoveImagesAsync(Fixture f, JsonElement dashboard)
    {
        var chartsOnly = dashboard.GetProperty("definition").GetProperty("widgets").EnumerateArray()
            .Where(w => w.GetProperty("kind").GetString() == "Chart")
            .Select(w => (object)new { id = w.GetProperty("id").GetGuid(), chartId = f.Chart, layout = new { x = 0, y = 0, w = 6, h = 5 } }).ToArray();
        return await f.Owner.PutAsJsonAsync(Url(f), new { name = "Sales", definition = new { widgets = chartsOnly, filters = Array.Empty<object>() } });
    }

    [Fact]
    public async Task Deleting_the_dashboard_removes_its_pictures()
    {
        var f = await NewFixtureAsync();
        var added = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png)));
        var second = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Jpeg, "b.jpg")));
        Assert.Equal(2, Images(second).Length);

        (await f.Owner.DeleteAsync(Url(f))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.GetAsync(ImageUrl(f, Images(added).Single()))).StatusCode);
    }

    [Fact]
    public async Task Viewers_see_pictures_but_cannot_add_or_replace_them()
    {
        var f = await NewFixtureAsync();
        var added = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png)));
        var widget = Images(added).Single();
        var viewer = await AddMemberAsync(f, "Viewer");

        Assert.Equal(Png, await (await viewer.GetAsync(ImageUrl(f, widget))).Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync(Url(f, "/image-widgets"), Form(Png))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PutAsync(Url(f, $"/image-widgets/{widget.GetProperty("id").GetGuid()}/image"), Form(Jpeg))).StatusCode);

        var (stranger, _, _) = await NewUserAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(ImageUrl(f, widget))).StatusCode);
    }

    [Fact]
    public async Task A_dashboard_holds_at_most_24_widgets_pictures_included()
    {
        var f = await NewFixtureAsync();
        for (var i = 0; i < 23; i++) (await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png))).EnsureSuccessStatusCode();

        var over = await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png));

        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
    }

    [Fact]
    public async Task A_visitor_with_a_link_sees_the_picture_widget_but_not_where_it_is_kept()
    {
        var f = await NewFixtureAsync();
        var added = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png, alt: "Logo", link: "https://example.com")));
        var widget = Images(added).Single();
        var id = widget.GetProperty("id").GetGuid();
        var link = await Read(await f.Owner.PostAsJsonAsync(Url(f, "/shares"), new { expiresInDays = 30 }));
        var token = link.GetProperty("token").GetString()!;
        var visitor = factory.CreateApiClient();

        var shared = await Read(await visitor.GetAsync($"/api/shared/dashboards/{token}"));
        var visible = shared.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("kind").GetString() == "Image");
        Assert.Equal("Logo", visible.GetProperty("alt").GetString());
        Assert.Equal("https://example.com", visible.GetProperty("linkUrl").GetString());
        Assert.DoesNotContain(".png", visible.GetProperty("imageKey").GetString()); // a version to ask for, not a file name
        Assert.Equal(1, shared.GetProperty("charts").GetArrayLength());

        var picture = await visitor.GetAsync($"/api/shared/dashboards/{token}/images/{id}");
        Assert.Equal(Png, await picture.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", picture.Headers.GetValues("X-Content-Type-Options").Single());

        var run = await Read(await visitor.PostAsJsonAsync($"/api/shared/dashboards/{token}/run", new { filters = new { } }));
        Assert.Single(run.GetProperty("widgets").EnumerateArray());

        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/shared/dashboards/{token}/images/{f.Chart}")).StatusCode);
        (await f.Owner.DeleteAsync(Url(f, $"/shares/{link.GetProperty("id").GetGuid()}"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/shared/dashboards/{token}/images/{id}")).StatusCode);
    }

    [Fact]
    public async Task Charts_still_work_beside_pictures_and_a_chart_can_still_only_be_on_once()
    {
        var f = await NewFixtureAsync();
        var added = await Read(await f.Owner.PostAsync(Url(f, "/image-widgets"), Form(Png)));

        var twice = await f.Owner.PutAsJsonAsync(Url(f), new
        {
            name = "Sales",
            definition = new
            {
                widgets = new object[] { new { id = Guid.NewGuid(), chartId = f.Chart }, new { id = Guid.NewGuid(), chartId = f.Chart } },
                filters = Array.Empty<object>(),
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);
        Assert.Single(Images(await Read(await f.Owner.GetAsync(Url(f)))));
        Assert.Single(Images(added));
    }
}
