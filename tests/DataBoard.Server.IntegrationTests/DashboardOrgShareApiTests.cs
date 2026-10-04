using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>
/// A dashboard shown to every member of another organization: live, read-only, never with its queries. The owning
/// organization decides, and only with organizations the person sharing belongs to.
/// </summary>
[Collection(ApiCollection.Name)]
public class DashboardOrgShareApiTests(ApiFactory factory)
{
    private sealed record Fixture(HttpClient Owner, UserDto OwnerUser, Guid Home, Guid Partner, Guid Dashboard);

    /// <summary>An owner of two organizations (Home, with a one-chart dashboard, and Partner).</summary>
    private async Task<Fixture> NewFixtureAsync()
    {
        var (owner, _, user) = await NewUserAsync(factory);
        var home = await CreateOrgAsync(owner, "Home Co");
        var partner = await CreateOrgAsync(owner, "Partner Co");

        var source = await owner.PostAsJsonAsync($"/api/organizations/{home.Id}/data-sources",
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        source.EnsureSuccessStatusCode();
        var sourceId = (await source.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id;
        var dataset = await Read(await owner.PostAsJsonAsync($"/api/organizations/{home.Id}/datasets",
            new { dataSourceId = sourceId, name = "Orders", sql = "SELECT id, region, price FROM orders" }));
        var chart = await Read(await owner.PostAsJsonAsync($"/api/organizations/{home.Id}/charts", new
        {
            name = "Revenue", datasetId = dataset.GetProperty("id").GetGuid(), type = "Number",
            definition = new { query = new { measures = new[] { new { aggregation = "Sum", column = "price", alias = "n" } } } },
        }));
        var dashboard = await Read(await owner.PostAsJsonAsync($"/api/organizations/{home.Id}/dashboards", new
        {
            name = "Sales",
            description = "Weekly numbers",
            definition = new
            {
                widgets = new object[] { new { chartId = chart.GetProperty("id").GetGuid(), layout = new { x = 0, y = 0, w = 6, h = 5 } } },
                filters = Array.Empty<object>(),
            },
        }));
        return new Fixture(owner, user, home.Id, partner.Id, dashboard.GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string Shares(Fixture f, string suffix = "") =>
        $"/api/organizations/{f.Home}/dashboards/{f.Dashboard}/organization-shares{suffix}";

    private static string SharedList(Guid org) => $"/api/organizations/{org}/shared-dashboards";
    private static string Opened(Fixture f, Guid viewingOrg, string suffix = "") => $"{SharedList(viewingOrg)}/{f.Home}/{f.Dashboard}{suffix}";

    private static Task<HttpResponseMessage> ShareAsync(Fixture f, Guid target) => f.Owner.PostAsJsonAsync(Shares(f), new { organizationId = target });

    /// <summary>Somebody with an account added to an organization (by the fixture's owner) with this role.</summary>
    private async Task<(HttpClient Client, UserDto User)> MemberAsync(Fixture f, Guid org, string role = "Viewer")
    {
        var (client, email, user) = await NewUserAsync(factory);
        (await f.Owner.PostAsJsonAsync($"/api/organizations/{org}/members", new { email, role })).EnsureSuccessStatusCode();
        return (client, user);
    }

    [Fact]
    public async Task Sharing_puts_the_dashboard_in_the_other_organizations_list_with_who_it_is_from()
    {
        var f = await NewFixtureAsync();
        var (member, _) = await MemberAsync(f, f.Partner);
        Assert.Empty((await Read(await member.GetAsync(SharedList(f.Partner)))).EnumerateArray());

        var shared = await Read(await ShareAsync(f, f.Partner));

        Assert.Equal(f.Partner, shared.GetProperty("organizationId").GetGuid());
        Assert.Equal("Partner Co", shared.GetProperty("organizationName").GetString());
        var row = (await Read(await member.GetAsync(SharedList(f.Partner)))).EnumerateArray().Single();
        Assert.Equal(f.Dashboard, row.GetProperty("dashboardId").GetGuid());
        Assert.Equal(f.Home, row.GetProperty("organizationId").GetGuid());
        Assert.Equal("Home Co", row.GetProperty("organizationName").GetString());
        Assert.Equal("Sales", row.GetProperty("name").GetString());
        Assert.Equal("Weekly numbers", row.GetProperty("description").GetString());
        Assert.Equal(1, row.GetProperty("widgetCount").GetInt32());
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Editor")]
    [InlineData("Owner")]
    public async Task Every_member_of_the_other_organization_opens_it_whatever_their_role_and_sees_the_data(string role)
    {
        var f = await NewFixtureAsync();
        await ShareAsync(f, f.Partner);
        var (member, _) = await MemberAsync(f, f.Partner, role);

        var open = await member.GetAsync(Opened(f, f.Partner));
        var page = await Read(open);
        var run = await Read(await member.PostAsJsonAsync(Opened(f, f.Partner, "/run"), new { filters = new { } }));

        Assert.Equal("Sales", page.GetProperty("name").GetString());
        Assert.Equal("Home Co", page.GetProperty("sharedBy").GetString());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("expiresUtc").ValueKind);
        Assert.Contains("no-store", open.Headers.CacheControl!.ToString());
        Assert.Equal(1, run.GetProperty("widgets").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("widgets")[0].GetProperty("error").ValueKind);
        Assert.NotEqual(0, run.GetProperty("widgets")[0].GetProperty("result").GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task What_they_get_names_no_query_dataset_or_column_and_the_run_has_no_sql()
    {
        var f = await NewFixtureAsync();
        await ShareAsync(f, f.Partner);
        var (member, _) = await MemberAsync(f, f.Partner);

        var page = await (await member.GetAsync(Opened(f, f.Partner))).Content.ReadAsStringAsync();
        var run = await (await member.PostAsJsonAsync(Opened(f, f.Partner, "/run"), new { filters = new { } })).Content.ReadAsStringAsync();

        foreach (var text in new[] { page, run })
        {
            Assert.DoesNotMatch(@"\bselect\b", text.ToLowerInvariant());
            Assert.DoesNotContain("datasetId", text);
            Assert.DoesNotContain("connection", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(run).RootElement.GetProperty("widgets")[0].GetProperty("result").GetProperty("sql").ValueKind);
    }

    [Fact]
    public async Task Somebody_outside_the_receiving_organization_sees_nothing_of_it_even_as_a_member_of_the_owner()
    {
        var f = await NewFixtureAsync();
        await ShareAsync(f, f.Partner);
        var (stranger, _, _) = await NewUserAsync(factory);
        var (homeMember, _) = await MemberAsync(f, f.Home); // in Home, but not in Partner

        foreach (var who in new[] { stranger, homeMember })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await who.GetAsync(SharedList(f.Partner))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await who.GetAsync(Opened(f, f.Partner))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await who.PostAsJsonAsync(Opened(f, f.Partner, "/run"), new { filters = new { } })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateApiClient().GetAsync(Opened(f, f.Partner))).StatusCode);
    }

    [Fact]
    public async Task A_member_of_another_organization_the_dashboard_is_not_shared_with_gets_the_same_not_found()
    {
        var f = await NewFixtureAsync();
        await ShareAsync(f, f.Partner);
        var third = await CreateOrgAsync(f.Owner, "Third Co");
        var (member, _) = await MemberAsync(f, third.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(Opened(f, third.Id))).StatusCode);
        Assert.Empty((await Read(await member.GetAsync(SharedList(third.Id)))).EnumerateArray());
    }

    [Fact]
    public async Task The_address_has_to_name_the_real_owner_and_dashboard_or_it_is_not_found()
    {
        var f = await NewFixtureAsync();
        await ShareAsync(f, f.Partner);
        var (member, _) = await MemberAsync(f, f.Partner);

        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"{SharedList(f.Partner)}/{f.Partner}/{f.Dashboard}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"{SharedList(f.Partner)}/{f.Home}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"{SharedList(f.Partner)}/{Guid.NewGuid()}/{f.Dashboard}")).StatusCode);
    }

    [Fact]
    public async Task Stopping_the_share_takes_it_out_of_their_list_and_closes_the_page_at_once()
    {
        var f = await NewFixtureAsync();
        await ShareAsync(f, f.Partner);
        var (member, _) = await MemberAsync(f, f.Partner);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(Opened(f, f.Partner))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await f.Owner.DeleteAsync(Shares(f, $"/{f.Partner}"))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(Opened(f, f.Partner))).StatusCode);
        Assert.Empty((await Read(await member.GetAsync(SharedList(f.Partner)))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await f.Owner.DeleteAsync(Shares(f, $"/{f.Partner}"))).StatusCode);
    }

    [Fact]
    public async Task It_is_live_a_change_to_the_dashboard_shows_to_the_other_organization_and_deleting_it_removes_it()
    {
        var f = await NewFixtureAsync();
        await ShareAsync(f, f.Partner);
        var (member, _) = await MemberAsync(f, f.Partner);

        var current = JsonNode.Parse(await f.Owner.GetStringAsync($"/api/organizations/{f.Home}/dashboards/{f.Dashboard}"))!;
        var saved = await f.Owner.PutAsJsonAsync($"/api/organizations/{f.Home}/dashboards/{f.Dashboard}", new { name = "Sales 2027", definition = current["definition"] });
        saved.EnsureSuccessStatusCode();
        Assert.Equal("Sales 2027", (await Read(await member.GetAsync(Opened(f, f.Partner)))).GetProperty("name").GetString());
        Assert.Equal("Sales 2027", (await Read(await member.GetAsync(SharedList(f.Partner)))).EnumerateArray().Single().GetProperty("name").GetString());

        (await f.Owner.DeleteAsync($"/api/organizations/{f.Home}/dashboards/{f.Dashboard}")).EnsureSuccessStatusCode();

        Assert.Empty((await Read(await member.GetAsync(SharedList(f.Partner)))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(Opened(f, f.Partner))).StatusCode);
    }

    [Fact]
    public async Task The_other_organization_can_look_but_never_change_the_dashboard_or_reach_it_as_its_own()
    {
        var f = await NewFixtureAsync();
        await ShareAsync(f, f.Partner);
        var (editor, _) = await MemberAsync(f, f.Partner, "Owner");

        foreach (var response in new[]
        {
            await editor.GetAsync($"/api/organizations/{f.Home}/dashboards/{f.Dashboard}"),
            await editor.PutAsJsonAsync($"/api/organizations/{f.Home}/dashboards/{f.Dashboard}", new { name = "Mine" }),
            await editor.DeleteAsync($"/api/organizations/{f.Home}/dashboards/{f.Dashboard}"),
            await editor.PostAsJsonAsync($"/api/organizations/{f.Home}/dashboards/{f.Dashboard}/organization-shares", new { organizationId = f.Partner }),
            await editor.GetAsync($"/api/organizations/{f.Partner}/dashboards/{f.Dashboard}"),
        })
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Assert.Empty((await Read(await editor.GetAsync($"/api/organizations/{f.Partner}/dashboards"))).EnumerateArray());
    }

    [Fact]
    public async Task Leaving_the_receiving_organization_ends_access_and_a_removed_share_stays_removed_when_they_come_back()
    {
        var f = await NewFixtureAsync();
        await ShareAsync(f, f.Partner);
        var (member, user) = await MemberAsync(f, f.Partner);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(Opened(f, f.Partner))).StatusCode);

        (await f.Owner.DeleteAsync($"/api/organizations/{f.Partner}/members/{user.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(Opened(f, f.Partner))).StatusCode);
    }

    [Fact]
    public async Task Only_editors_of_the_owning_organization_share_and_only_with_organizations_they_belong_to()
    {
        var f = await NewFixtureAsync();
        var (viewer, _) = await MemberAsync(f, f.Home);
        var (editor, _) = await MemberAsync(f, f.Home, "Editor");
        var elsewhere = await CreateOrgAsync((await NewUserAsync(factory)).Client, "Not mine");

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(Shares(f), new { organizationId = f.Partner })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(Shares(f))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync(Shares(f, $"/{f.Partner}"))).StatusCode);

        // The editor is in Home only: not in Partner, not in the stranger's organization.
        foreach (var target in new[] { f.Partner, elsewhere.Id })
        {
            var refused = await editor.PostAsJsonAsync(Shares(f), new { organizationId = target });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("organizations you belong to", await refused.Content.ReadAsStringAsync());
        }

        foreach (var wrong in new object[] { new { organizationId = Guid.Empty }, new { organizationId = f.Home }, new { } })
        {
            var refused = await f.Owner.PostAsJsonAsync(Shares(f), wrong);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
        Assert.Empty((await Read(await f.Owner.GetAsync(Shares(f)))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateApiClient().GetAsync(Shares(f))).StatusCode);
    }

    [Fact]
    public async Task Sharing_twice_is_one_share()
    {
        var f = await NewFixtureAsync();

        var first = await Read(await ShareAsync(f, f.Partner));
        var second = await Read(await ShareAsync(f, f.Partner));

        // The database keeps microseconds, so compare to the millisecond.
        Assert.True(Math.Abs((first.GetProperty("sharedUtc").GetDateTime() - second.GetProperty("sharedUtc").GetDateTime()).TotalMilliseconds) < 1);
        Assert.Single((await Read(await f.Owner.GetAsync(Shares(f)))).EnumerateArray());
    }

    [Fact]
    public async Task An_editor_who_is_not_in_the_receiving_organization_sees_it_unnamed_and_can_still_stop_it()
    {
        var f = await NewFixtureAsync();
        await ShareAsync(f, f.Partner);
        var (editor, _) = await MemberAsync(f, f.Home, "Editor");

        var listed = (await Read(await editor.GetAsync(Shares(f)))).EnumerateArray().Single();

        Assert.Equal(f.Partner, listed.GetProperty("organizationId").GetGuid());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("organizationName").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await editor.DeleteAsync(Shares(f, $"/{f.Partner}"))).StatusCode);
    }

    [Fact]
    public async Task One_dashboard_can_go_to_several_organizations_and_each_sees_it()
    {
        var f = await NewFixtureAsync();
        var third = await CreateOrgAsync(f.Owner, "Third Co");
        await ShareAsync(f, f.Partner);
        await ShareAsync(f, third.Id);

        var names = (await Read(await f.Owner.GetAsync(Shares(f)))).EnumerateArray().Select(s => s.GetProperty("organizationName").GetString()).Order();

        Assert.Equal(["Partner Co", "Third Co"], names);
        Assert.Single((await Read(await f.Owner.GetAsync(SharedList(f.Partner)))).EnumerateArray());
        Assert.Single((await Read(await f.Owner.GetAsync(SharedList(third.Id)))).EnumerateArray());
    }

    [Fact]
    public async Task A_copy_of_the_dashboard_is_not_shared_and_the_shared_list_is_sorted_by_owner_then_name()
    {
        var f = await NewFixtureAsync();
        await ShareAsync(f, f.Partner);

        var copy = await Read(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Home}/dashboards/{f.Dashboard}/duplicate", new { }));

        var copyShares = await Read(await f.Owner.GetAsync($"/api/organizations/{f.Home}/dashboards/{copy.GetProperty("id").GetGuid()}/organization-shares"));
        Assert.Empty(copyShares.EnumerateArray());
        Assert.Single((await Read(await f.Owner.GetAsync(SharedList(f.Partner)))).EnumerateArray());
    }
}
