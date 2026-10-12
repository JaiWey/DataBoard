using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>
/// Who may see a dashboard or a dashboard folder. A hidden thing is simply not found: not listed, opened, run, changed,
/// shared or exported. Owners always see everything; a rule on a folder covers everything inside it.
/// </summary>
[Collection(ApiCollection.Name)]
public class DashboardAccessApiTests(ApiFactory factory)
{
    private sealed record Person(HttpClient Client, UserDto User);

    private sealed record Fixture(Guid Org, Guid Chart, Person Owner, Person Editor, Person Viewer, Person OtherViewer);

    private async Task<Fixture> NewFixtureAsync()
    {
        var (owner, _, ownerUser) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner, "Acme");

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

        async Task<Person> Join(string role)
        {
            var (client, email, user) = await NewUserAsync(factory);
            (await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/members", new { email, role })).EnsureSuccessStatusCode();
            return new Person(client, user);
        }

        return new Fixture(org.Id, chart.GetProperty("id").GetGuid(), new Person(owner, ownerUser), await Join("Editor"), await Join("Viewer"), await Join("Viewer"));
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string Dash(Fixture f, Guid id, string suffix = "") => $"/api/organizations/{f.Org}/dashboards/{id}{suffix}";
    private static string Folders(Fixture f, string suffix = "") => $"/api/organizations/{f.Org}/folders{suffix}";

    private static async Task<Guid> DashboardAsync(Fixture f, string name, Guid? folder = null, HttpClient? by = null)
    {
        var response = await (by ?? f.Owner.Client).PostAsJsonAsync($"/api/organizations/{f.Org}/dashboards", new
        {
            name,
            folderId = folder,
            definition = new
            {
                widgets = new object[] { new { chartId = f.Chart, layout = new { x = 0, y = 0, w = 6, h = 5 } } },
                filters = Array.Empty<object>(),
            },
        });
        return (await Read(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> FolderAsync(Fixture f, string name, Guid? parent = null) =>
        (await Read(await f.Owner.Client.PostAsJsonAsync(Folders(f), new { name, parentId = parent, kind = "Dashboard" }))).GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> LimitAsync(Fixture f, Guid dashboard, object rule, HttpClient? by = null) =>
        (by ?? f.Owner.Client).PutAsJsonAsync(Dash(f, dashboard, "/access"), rule);

    private static Task<HttpResponseMessage> LimitFolderAsync(Fixture f, Guid folder, object rule, HttpClient? by = null) =>
        (by ?? f.Owner.Client).PutAsJsonAsync(Folders(f, $"/{folder}/access"), rule);

    private static async Task<Guid[]> NamesSeenAsync(Fixture f, HttpClient who, string what = "dashboards")
    {
        var list = await Read(await who.GetAsync($"/api/organizations/{f.Org}/{what}"));
        return list.EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToArray();
    }

    private static readonly object EditorsUp = new { minRole = "Editor", memberIds = Array.Empty<Guid>() };

    [Fact]
    public async Task A_dashboard_nobody_limited_is_seen_by_everyone_and_says_it_is_not_restricted()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Open");

        foreach (var who in new[] { f.Owner, f.Editor, f.Viewer })
            Assert.Contains(d, await NamesSeenAsync(f, who.Client));
        var dto = await Read(await f.Viewer.Client.GetAsync(Dash(f, d)));
        Assert.False(dto.GetProperty("restricted").GetBoolean());
        var access = await Read(await f.Editor.Client.GetAsync(Dash(f, d, "/access")));
        Assert.Equal(JsonValueKind.Null, access.GetProperty("minRole").ValueKind);
        Assert.Equal(0, access.GetProperty("memberIds").GetArrayLength());
    }

    [Fact]
    public async Task A_minimum_role_hides_the_dashboard_from_everybody_below_it()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Staff only");
        var other = await DashboardAsync(f, "For all");
        Assert.Equal(HttpStatusCode.OK, (await LimitAsync(f, d, EditorsUp)).StatusCode);

        Assert.DoesNotContain(d, await NamesSeenAsync(f, f.Viewer.Client));
        Assert.Contains(other, await NamesSeenAsync(f, f.Viewer.Client));
        foreach (var who in new[] { f.Owner, f.Editor })
            Assert.Contains(d, await NamesSeenAsync(f, who.Client));
        Assert.True((await Read(await f.Owner.Client.GetAsync(Dash(f, d)))).GetProperty("restricted").GetBoolean());
    }

    [Fact]
    public async Task A_hidden_dashboard_is_not_found_for_every_way_of_reaching_it()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Hidden");
        await LimitAsync(f, d, EditorsUp);
        var viewer = f.Viewer.Client;
        var folder = await FolderAsync(f, "Some folder");

        var attempts = new[]
        {
            await viewer.GetAsync(Dash(f, d)),
            await viewer.PostAsJsonAsync(Dash(f, d, "/run"), new { filters = new { } }),
            await viewer.GetAsync(Dash(f, d, "/background")),
            await viewer.GetAsync(Dash(f, d, $"/image-widgets/{Guid.NewGuid()}/image")),
            await viewer.GetAsync(Dash(f, d, "/access")),
            await viewer.GetAsync(Dash(f, d, "/export")),
            await viewer.GetAsync(Dash(f, d, "/shares")),
            await viewer.GetAsync(Dash(f, d, "/organization-shares")),
        };
        foreach (var response in attempts) Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden });
        Assert.Equal(HttpStatusCode.NotFound, attempts[0].StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, attempts[1].StatusCode);

        // The ones that need the Editor role: an editor who is let in does these; one who is not gets "not found", never "forbidden".
        await LimitAsync(f, d, new { minRole = "Owner", memberIds = Array.Empty<Guid>() });
        var editor = f.Editor.Client;
        foreach (var response in new[]
        {
            await editor.GetAsync(Dash(f, d)),
            await editor.PostAsJsonAsync(Dash(f, d, "/run"), new { filters = new { } }),
            await editor.PutAsJsonAsync(Dash(f, d), new { name = "Mine" }),
            await editor.DeleteAsync(Dash(f, d)),
            await editor.PostAsJsonAsync(Dash(f, d, "/duplicate"), new { }),
            await editor.GetAsync(Dash(f, d, "/export")),
            await editor.PutAsJsonAsync(Dash(f, d, "/folder"), new { folderId = folder }),
            await editor.PostAsJsonAsync(Dash(f, d, "/shares"), new { expiresInDays = 7 }),
            await editor.GetAsync(Dash(f, d, "/shares")),
            await editor.PostAsJsonAsync(Dash(f, d, "/organization-shares"), new { organizationId = f.Org }),
            await editor.GetAsync(Dash(f, d, "/organization-shares")),
            await editor.PutAsJsonAsync(Dash(f, d, "/refresh"), new { intervalMinutes = 15 }),
            await editor.PutAsJsonAsync(Dash(f, d, "/access"), EditorsUp),
        })
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Nothing it tried changed it.
        Assert.Equal("Hidden", (await Read(await f.Owner.Client.GetAsync(Dash(f, d)))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Named_people_get_in_whatever_their_role_and_others_with_the_same_role_do_not()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "For one viewer");

        Assert.Equal(HttpStatusCode.OK, (await LimitAsync(f, d, new { minRole = (string?)null, memberIds = new[] { f.Viewer.User.Id } })).StatusCode);

        Assert.Contains(d, await NamesSeenAsync(f, f.Viewer.Client));
        Assert.DoesNotContain(d, await NamesSeenAsync(f, f.OtherViewer.Client));
        Assert.DoesNotContain(d, await NamesSeenAsync(f, f.Editor.Client)); // an Editor is not listed and no role was let in
        Assert.Contains(d, await NamesSeenAsync(f, f.Owner.Client)); // Owners always see
        Assert.Equal(HttpStatusCode.OK, (await f.Viewer.Client.GetAsync(Dash(f, d))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Viewer.Client.PostAsJsonAsync(Dash(f, d, "/run"), new { filters = new { } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.OtherViewer.Client.GetAsync(Dash(f, d))).StatusCode);
    }

    [Fact]
    public async Task A_role_or_a_listed_person_is_enough()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Role or name");

        await LimitAsync(f, d, new { minRole = "Editor", memberIds = new[] { f.Viewer.User.Id } });

        Assert.Contains(d, await NamesSeenAsync(f, f.Editor.Client));
        Assert.Contains(d, await NamesSeenAsync(f, f.Viewer.Client));
        Assert.DoesNotContain(d, await NamesSeenAsync(f, f.OtherViewer.Client));
    }

    [Fact]
    public async Task Owners_see_everything_even_when_a_rule_names_only_somebody_else()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Not for the owner");

        await LimitAsync(f, d, new { memberIds = new[] { f.Viewer.User.Id } });

        Assert.Equal(HttpStatusCode.OK, (await f.Owner.Client.GetAsync(Dash(f, d))).StatusCode);
    }

    [Fact]
    public async Task A_rule_on_a_folder_hides_the_folder_what_is_in_it_and_the_folders_inside()
    {
        var f = await NewFixtureAsync();
        var finance = await FolderAsync(f, "Finance");
        var nested = await FolderAsync(f, "Budgets", finance);
        var inFinance = await DashboardAsync(f, "In finance", finance);
        var inNested = await DashboardAsync(f, "Deep in finance", nested);
        var outside = await DashboardAsync(f, "Outside");

        Assert.Equal(HttpStatusCode.OK, (await LimitFolderAsync(f, finance, EditorsUp)).StatusCode);

        var seen = await NamesSeenAsync(f, f.Viewer.Client);
        Assert.Equal([outside], seen);
        var folders = await NamesSeenAsync(f, f.Viewer.Client, "folders?kind=Dashboard");
        Assert.Empty(folders);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Viewer.Client.GetAsync(Dash(f, inNested))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Viewer.Client.PostAsJsonAsync(Dash(f, inFinance, "/run"), new { filters = new { } })).StatusCode);

        Assert.Equal(3, (await NamesSeenAsync(f, f.Editor.Client)).Length);
        Assert.Equal(2, (await NamesSeenAsync(f, f.Editor.Client, "folders?kind=Dashboard")).Length);
        Assert.True((await Read(await f.Editor.Client.GetAsync(Folders(f, "?kind=Dashboard")))).EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == finance).GetProperty("restricted").GetBoolean());
    }

    [Fact]
    public async Task The_strictest_rule_on_the_way_wins_and_a_dashboard_cannot_be_opened_up_beyond_its_folder()
    {
        var f = await NewFixtureAsync();
        var folder = await FolderAsync(f, "Staff");
        var d = await DashboardAsync(f, "Open dashboard in a staff folder", folder);
        await LimitFolderAsync(f, folder, EditorsUp);

        Assert.DoesNotContain(d, await NamesSeenAsync(f, f.Viewer.Client));
        // The dashboard's own rule lets the viewer in by name, but the folder still keeps the viewer out.
        await LimitAsync(f, d, new { memberIds = new[] { f.Viewer.User.Id } });
        Assert.DoesNotContain(d, await NamesSeenAsync(f, f.Viewer.Client));

        // And the other way round: the folder lets the editor in, the dashboard's own rule keeps the editor out.
        await LimitAsync(f, d, new { minRole = "Owner" });
        Assert.DoesNotContain(d, await NamesSeenAsync(f, f.Editor.Client));
        Assert.Contains(d, await NamesSeenAsync(f, f.Owner.Client));
    }

    [Fact]
    public async Task What_is_inherited_is_told_with_the_name_of_the_folder_and_nothing_hidden_is_named()
    {
        var f = await NewFixtureAsync();
        var folder = await FolderAsync(f, "Finance");
        var d = await DashboardAsync(f, "Plan", folder);
        await LimitFolderAsync(f, folder, new { minRole = "Editor", memberIds = new[] { f.Viewer.User.Id } });

        var access = await Read(await f.Editor.Client.GetAsync(Dash(f, d, "/access")));

        var inherited = access.GetProperty("inherited").EnumerateArray().Single();
        Assert.Equal("Finance", inherited.GetProperty("folderName").GetString());
        Assert.Equal("Editor", inherited.GetProperty("minRole").GetString());
        Assert.Equal(1, inherited.GetProperty("people").GetInt32());
        Assert.Equal(0, access.GetProperty("memberIds").GetArrayLength());
    }

    [Fact]
    public async Task Making_a_dashboard_where_you_may_not_look_is_refused()
    {
        var f = await NewFixtureAsync();
        var hidden = await FolderAsync(f, "Hidden");
        await LimitFolderAsync(f, hidden, new { minRole = "Owner" });
        var existing = await DashboardAsync(f, "Existing");

        var create = await f.Editor.Client.PostAsJsonAsync($"/api/organizations/{f.Org}/dashboards", new
        {
            name = "Sneaky", folderId = hidden, definition = new { widgets = Array.Empty<object>(), filters = Array.Empty<object>() },
        });
        var moveIn = await f.Editor.Client.PutAsJsonAsync(Dash(f, existing, "/folder"), new { folderId = hidden });
        var folderIn = await f.Editor.Client.PostAsJsonAsync(Folders(f), new { name = "Sub", parentId = hidden, kind = "Dashboard" });

        foreach (var response in new[] { create, moveIn, folderIn })
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("That folder does not exist.", await create.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, (await f.Owner.Client.PostAsJsonAsync(Folders(f), new { name = "Sub", parentId = hidden, kind = "Dashboard" })).StatusCode);
    }

    [Fact]
    public async Task A_folder_somebody_may_not_see_cannot_be_renamed_moved_or_deleted_by_them()
    {
        var f = await NewFixtureAsync();
        var other = await FolderAsync(f, "Elsewhere");
        var hidden = await FolderAsync(f, "Hidden");
        await LimitFolderAsync(f, hidden, new { minRole = "Owner" });
        var editor = f.Editor.Client;

        Assert.Equal(HttpStatusCode.NotFound, (await editor.PutAsJsonAsync(Folders(f, $"/{hidden}"), new { name = "Mine", parentId = (Guid?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await editor.DeleteAsync(Folders(f, $"/{hidden}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync(Folders(f, $"/{hidden}/access"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PutAsJsonAsync(Folders(f, $"/{other}"), new { name = "Elsewhere", parentId = hidden })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await f.Owner.Client.DeleteAsync(Folders(f, $"/{hidden}"))).StatusCode);
    }

    [Fact]
    public async Task Only_dashboard_folders_can_be_limited()
    {
        var f = await NewFixtureAsync();
        var dataset = await Read(await f.Owner.Client.PostAsJsonAsync(Folders(f), new { name = "Data", kind = "Dataset" }));
        var chart = await Read(await f.Owner.Client.PostAsJsonAsync(Folders(f), new { name = "Charts", kind = "Chart" }));

        foreach (var folder in new[] { dataset, chart })
        {
            var id = folder.GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.NotFound, (await LimitFolderAsync(f, id, EditorsUp)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.Client.GetAsync(Folders(f, $"/{id}/access"))).StatusCode);
        }
    }

    [Fact]
    public async Task Only_editors_and_owners_who_can_see_it_change_who_can()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Rule me");

        Assert.Equal(HttpStatusCode.Forbidden, (await LimitAsync(f, d, EditorsUp, f.Viewer.Client)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Viewer.Client.GetAsync(Dash(f, d, "/access"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LimitAsync(f, d, EditorsUp, f.Editor.Client)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateApiClient().GetAsync(Dash(f, d, "/access"))).StatusCode);
    }

    [Fact]
    public async Task A_rule_that_would_lock_the_person_out_is_refused_unless_they_are_an_owner()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Careful");

        foreach (var rule in new object[]
        {
            new { minRole = "Owner" },
            new { memberIds = new[] { f.Viewer.User.Id } },
        })
        {
            var refused = await LimitAsync(f, d, rule, f.Editor.Client);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("lock you out", await refused.Content.ReadAsStringAsync());
        }

        Assert.Equal(HttpStatusCode.OK, (await LimitAsync(f, d, new { memberIds = new[] { f.Editor.User.Id } }, f.Editor.Client)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LimitAsync(f, d, new { memberIds = new[] { f.Viewer.User.Id } }, f.Owner.Client)).StatusCode); // owners are never locked out
    }

    [Theory]
    [InlineData("""{ "minRole": "Boss" }""")]
    [InlineData("""{ "minRole": 0 }""")]
    [InlineData("""{ "minRole": 9 }""")]
    [InlineData("""{ "memberIds": ["00000000-0000-0000-0000-000000000000"] }""")]
    public async Task A_rule_that_is_not_one_is_refused_and_nothing_changes(string body)
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Plain");

        var response = await f.Owner.Client.PutAsync(Dash(f, d, "/access"), new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(d, await NamesSeenAsync(f, f.Viewer.Client));
    }

    [Fact]
    public async Task Only_people_of_this_organization_can_be_listed_and_not_too_many()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Names");
        var (_, _, stranger) = await NewUserAsync(factory);

        var outsider = await LimitAsync(f, d, new { memberIds = new[] { stranger.Id } });
        Assert.Equal(HttpStatusCode.BadRequest, outsider.StatusCode);
        Assert.Contains("members of this organization", await outsider.Content.ReadAsStringAsync());

        var many = await LimitAsync(f, d, new { memberIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, many.StatusCode);
        Assert.Contains(d, await NamesSeenAsync(f, f.Viewer.Client));
    }

    [Fact]
    public async Task Clearing_the_rule_opens_it_to_everybody_again()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Open again");
        await LimitAsync(f, d, EditorsUp);
        Assert.DoesNotContain(d, await NamesSeenAsync(f, f.Viewer.Client));

        Assert.Equal(HttpStatusCode.OK, (await LimitAsync(f, d, new { })).StatusCode);

        Assert.Contains(d, await NamesSeenAsync(f, f.Viewer.Client));
        Assert.False((await Read(await f.Viewer.Client.GetAsync(Dash(f, d)))).GetProperty("restricted").GetBoolean());
    }

    [Fact]
    public async Task A_copy_of_a_limited_dashboard_is_limited_the_same_way()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Original");
        await LimitAsync(f, d, new { minRole = "Editor", memberIds = new[] { f.Viewer.User.Id } });

        var copy = (await Read(await f.Editor.Client.PostAsJsonAsync(Dash(f, d, "/duplicate"), new { }))).GetProperty("id").GetGuid();

        var access = await Read(await f.Owner.Client.GetAsync(Dash(f, copy, "/access")));
        Assert.Equal("Editor", access.GetProperty("minRole").GetString());
        Assert.Equal(f.Viewer.User.Id, access.GetProperty("memberIds")[0].GetGuid());
        Assert.DoesNotContain(copy, await NamesSeenAsync(f, f.OtherViewer.Client));
    }

    [Fact]
    public async Task A_link_or_a_share_made_before_stays_what_it_was_an_explicit_grant()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Linked");
        var link = await Read(await f.Owner.Client.PostAsJsonAsync(Dash(f, d, "/shares"), new { expiresInDays = 7 }));

        await LimitAsync(f, d, new { minRole = "Owner" });

        Assert.Equal(HttpStatusCode.OK, (await factory.CreateApiClient().GetAsync($"/api/shared/dashboards/{link.GetProperty("token").GetString()}")).StatusCode);
    }

    [Fact]
    public async Task The_hidden_dashboard_is_not_in_the_dashboards_a_chart_is_used_on_and_not_through_the_mcp_list()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Secret plan");
        await LimitAsync(f, d, EditorsUp);

        var summaries = await Read(await f.Viewer.Client.GetAsync($"/api/organizations/{f.Org}/dashboards"));

        Assert.DoesNotContain(summaries.EnumerateArray(), s => s.GetProperty("name").GetString() == "Secret plan");
        Assert.DoesNotContain("Secret plan", summaries.GetRawText());
    }

    [Fact]
    public async Task Somebody_who_is_removed_from_the_organization_or_never_in_it_sees_nothing_either_way()
    {
        var f = await NewFixtureAsync();
        var d = await DashboardAsync(f, "Open to the organization");
        var (stranger, _, _) = await NewUserAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Dash(f, d))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Dash(f, d, "/access"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync(Dash(f, d, "/access"), EditorsUp)).StatusCode);
    }
}
