using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>The picture behind a dashboard: who can set it, what is accepted, and who can see it (members, and link visitors).</summary>
[Collection(ApiCollection.Name)]
public class DashboardBackgroundApiTests(ApiFactory factory)
{
    // A real 1x1 PNG, and the first bytes of the other kinds (enough to be recognised, which is all that is checked).
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0];
    private static readonly byte[] Gif = "GIF89a\u0001\0\u0001\0\0\0\0;"u8.ToArray();
    private static readonly byte[] Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

    private sealed record Fixture(HttpClient Owner, Guid Org, Guid Dashboard);

    private static string Url(Fixture f, string suffix = "") => $"/api/organizations/{f.Org}/dashboards/{f.Dashboard}{suffix}";

    private async Task<Fixture> NewFixtureAsync()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var response = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/dashboards",
            new { name = "Sales", definition = new { widgets = Array.Empty<object>(), filters = Array.Empty<object>() } });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var dashboard = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        return new Fixture(owner, org.Id, dashboard);
    }

    private static MultipartFormDataContent Form(byte[]? image, string fileName = "picture.png", string? fit = null, int? opacity = null)
    {
        var form = new MultipartFormDataContent();
        if (image is not null)
        {
            var file = new ByteArrayContent(image);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png"); // what a browser says; never trusted
            form.Add(file, "File", fileName);
        }
        if (fit is not null) form.Add(new StringContent(fit), "Fit");
        if (opacity is not null) form.Add(new StringContent(opacity.Value.ToString()), "Opacity");
        return form;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<HttpClient> AddMemberAsync(Fixture f, string role)
    {
        var (member, email, _) = await NewUserAsync(factory);
        (await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/members", new { email, role })).EnsureSuccessStatusCode();
        return member;
    }

    [Fact]
    public async Task A_dashboard_has_no_background_until_one_is_set()
    {
        var f = await NewFixtureAsync();

        var dashboard = await ReadAsync(await f.Owner.GetAsync(Url(f)));

        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("background").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.GetAsync(Url(f, "/background"))).StatusCode);
    }

    [Fact]
    public async Task An_editor_sets_a_picture_and_members_get_the_same_bytes_back_with_its_type()
    {
        var f = await NewFixtureAsync();

        var set = await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Png, fit: "contain", opacity: 60)));

        var background = set.GetProperty("background");
        Assert.Equal("contain", background.GetProperty("fit").GetString());
        Assert.Equal(60, background.GetProperty("opacity").GetInt32());
        Assert.Matches("^[0-9a-f]{32}$", background.GetProperty("version").GetString());

        var viewer = await AddMemberAsync(f, "Viewer");
        var picture = await viewer.GetAsync(Url(f, "/background"));
        Assert.Equal("image/png", picture.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await picture.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", picture.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("immutable", picture.Headers.CacheControl!.ToString());

        // The dashboard carries it, in its own list too.
        var fetched = await ReadAsync(await viewer.GetAsync(Url(f)));
        Assert.Equal(background.GetProperty("version").GetString(), fetched.GetProperty("background").GetProperty("version").GetString());
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("gif")]
    public async Task Other_kinds_of_picture_are_kept_with_their_own_type(string kind)
    {
        var f = await NewFixtureAsync();

        await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(kind == "jpeg" ? Jpeg : Gif, fileName: "x." + kind)));

        var picture = await f.Owner.GetAsync(Url(f, "/background"));
        Assert.Equal("image/" + kind, picture.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task What_is_not_a_picture_is_refused_whatever_it_is_called_and_nothing_is_kept()
    {
        var f = await NewFixtureAsync();

        var svg = await f.Owner.PutAsync(Url(f, "/background"), Form(Svg, fileName: "logo.svg"));
        var disguised = await f.Owner.PutAsync(Url(f, "/background"), Form(Svg, fileName: "logo.png"));
        var empty = await f.Owner.PutAsync(Url(f, "/background"), Form(Array.Empty<byte>()));
        var nothing = await f.Owner.PutAsync(Url(f, "/background"), Form(null));

        Assert.All([svg, disguised, empty, nothing], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.GetAsync(Url(f, "/background"))).StatusCode);
    }

    [Fact]
    public async Task A_picture_over_the_limit_is_refused()
    {
        var f = await NewFixtureAsync();
        var big = new byte[5 * 1024 * 1024 + 10];
        Png.CopyTo(big, 0);

        var response = await f.Owner.PutAsync(Url(f, "/background"), Form(big));

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, response.StatusCode.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.GetAsync(Url(f, "/background"))).StatusCode);
    }

    [Fact]
    public async Task How_it_is_shown_can_change_without_a_new_picture_and_the_picture_stays()
    {
        var f = await NewFixtureAsync();
        var first = await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Png)));

        var changed = await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(null, fit: "tile", opacity: 30)));

        Assert.Equal("tile", changed.GetProperty("background").GetProperty("fit").GetString());
        Assert.Equal(30, changed.GetProperty("background").GetProperty("opacity").GetInt32());
        Assert.Equal(first.GetProperty("background").GetProperty("version").GetString(), changed.GetProperty("background").GetProperty("version").GetString());
        Assert.Equal(HttpStatusCode.OK, (await f.Owner.GetAsync(Url(f, "/background"))).StatusCode);
    }

    [Fact]
    public async Task Changing_how_it_is_shown_with_no_picture_set_is_refused_and_so_are_bad_values()
    {
        var f = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await f.Owner.PutAsync(Url(f, "/background"), Form(null, fit: "tile"))).StatusCode);

        await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Png)));
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Owner.PutAsync(Url(f, "/background"), Form(null, fit: "stretch"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Owner.PutAsync(Url(f, "/background"), Form(null, opacity: 5))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Owner.PutAsync(Url(f, "/background"), Form(null, opacity: 101))).StatusCode);
        var unchanged = await ReadAsync(await f.Owner.GetAsync(Url(f)));
        Assert.Equal("cover", unchanged.GetProperty("background").GetProperty("fit").GetString());
    }

    [Fact]
    public async Task A_new_picture_replaces_the_old_one_and_gets_a_new_version()
    {
        var f = await NewFixtureAsync();
        var first = await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Png)));

        var second = await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Jpeg, fileName: "b.jpg")));

        Assert.NotEqual(first.GetProperty("background").GetProperty("version").GetString(), second.GetProperty("background").GetProperty("version").GetString());
        Assert.Equal("image/jpeg", (await f.Owner.GetAsync(Url(f, "/background"))).Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Removing_the_background_takes_the_picture_away()
    {
        var f = await NewFixtureAsync();
        await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Png, fit: "tile", opacity: 50)));

        var removed = await ReadAsync(await f.Owner.DeleteAsync(Url(f, "/background")));

        Assert.Equal(JsonValueKind.Null, removed.GetProperty("background").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.GetAsync(Url(f, "/background"))).StatusCode);
        // A picture set afterwards starts from the usual way of showing it.
        var again = await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Png)));
        Assert.Equal("cover", again.GetProperty("background").GetProperty("fit").GetString());
    }

    [Fact]
    public async Task Editing_the_dashboard_keeps_its_background()
    {
        var f = await NewFixtureAsync();
        await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Png)));

        var edited = await ReadAsync(await f.Owner.PutAsJsonAsync(Url(f),
            new { name = "Sales 2", definition = new { widgets = Array.Empty<object>(), filters = Array.Empty<object>() } }));

        Assert.Equal("Sales 2", edited.GetProperty("name").GetString());
        Assert.NotEqual(JsonValueKind.Null, edited.GetProperty("background").ValueKind);
    }

    [Fact]
    public async Task Viewers_can_see_it_but_not_change_it()
    {
        var f = await NewFixtureAsync();
        await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Png)));
        var viewer = await AddMemberAsync(f, "Viewer");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Url(f, "/background"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsync(Url(f, "/background"), Form(Jpeg))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync(Url(f, "/background"))).StatusCode);
    }

    [Fact]
    public async Task Other_organizations_and_visitors_without_a_link_see_nothing()
    {
        var f = await NewFixtureAsync();
        await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Png)));
        var (stranger, _, _) = await NewUserAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Url(f, "/background"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateApiClient().GetAsync(Url(f, "/background"))).StatusCode);
    }

    [Fact]
    public async Task A_visitor_with_a_link_sees_the_picture_until_the_link_is_revoked()
    {
        var f = await NewFixtureAsync();
        await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Png, fit: "center", opacity: 80)));
        var link = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f, "/shares"), new { expiresInDays = 30 }));
        var token = link.GetProperty("token").GetString()!;
        var visitor = factory.CreateApiClient();

        var shared = await ReadAsync(await visitor.GetAsync($"/api/shared/dashboards/{token}"));
        Assert.Equal("center", shared.GetProperty("background").GetProperty("fit").GetString());
        Assert.Equal(80, shared.GetProperty("background").GetProperty("opacity").GetInt32());

        var picture = await visitor.GetAsync($"/api/shared/dashboards/{token}/background");
        Assert.Equal(Png, await picture.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", picture.Headers.GetValues("X-Content-Type-Options").Single());

        (await f.Owner.DeleteAsync(Url(f, $"/shares/{link.GetProperty("id").GetGuid()}"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/shared/dashboards/{token}/background")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/api/shared/dashboards/not-a-real-token/background")).StatusCode);
    }

    [Fact]
    public async Task A_link_to_a_dashboard_without_a_background_has_none_to_fetch()
    {
        var f = await NewFixtureAsync();
        var link = await ReadAsync(await f.Owner.PostAsJsonAsync(Url(f, "/shares"), new { expiresInDays = 30 }));
        var token = link.GetProperty("token").GetString()!;
        var visitor = factory.CreateApiClient();

        var shared = await ReadAsync(await visitor.GetAsync($"/api/shared/dashboards/{token}"));

        Assert.Equal(JsonValueKind.Null, shared.GetProperty("background").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/shared/dashboards/{token}/background")).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_dashboard_removes_the_picture_too()
    {
        var f = await NewFixtureAsync();
        await ReadAsync(await f.Owner.PutAsync(Url(f, "/background"), Form(Png)));

        (await f.Owner.DeleteAsync(Url(f))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.GetAsync(Url(f, "/background"))).StatusCode);
    }
}
