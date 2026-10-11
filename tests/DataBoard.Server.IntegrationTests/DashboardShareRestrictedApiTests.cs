using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DataBoard.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>
/// Links limited to signed-in members of chosen organizations: nobody signed in is asked to sign in (401), somebody who is
/// not in one of the organizations is refused (403), and members are let in whatever their role. Wrong links stay the same 404.
/// </summary>
[Collection(ApiCollection.Name)]
public class DashboardShareRestrictedApiTests(ApiFactory factory)
{
    private sealed record Fixture(HttpClient Owner, Guid Org, Guid OtherOrg, Guid Dashboard);

    /// <summary>An owner of two organizations and a one-chart dashboard in the first.</summary>
    private async Task<Fixture> NewFixtureAsync()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner, "Home");
        var other = await CreateOrgAsync(owner, "Partner");

        var source = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        source.EnsureSuccessStatusCode();
        var sourceId = (await source.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id;
        var dataset = await ReadAsync(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/datasets",
            new { dataSourceId = sourceId, name = "Orders", sql = "SELECT id, region, price FROM orders" }));
        var chart = await ReadAsync(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/charts", new
        {
            name = "Revenue", datasetId = dataset.GetProperty("id").GetGuid(), type = "Number",
            definition = new { query = new { measures = new[] { new { aggregation = "Sum", column = "price", alias = "n" } } } },
        }));
        var dashboard = await ReadAsync(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/dashboards", new
        {
            name = "Sales",
            definition = new
            {
                widgets = new object[] { new { chartId = chart.GetProperty("id").GetGuid(), layout = new { x = 0, y = 0, w = 6, h = 5 } } },
                filters = Array.Empty<object>(),
            },
        }));
        return new Fixture(owner, org.Id, other.Id, dashboard.GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string Shares(Fixture f, string suffix = "") => $"/api/organizations/{f.Org}/dashboards/{f.Dashboard}/shares{suffix}";

    private static async Task<(Guid Id, string Token)> LinkAsync(Fixture f, params Guid[] organizations)
    {
        var created = await ReadAsync(await f.Owner.PostAsJsonAsync(Shares(f), new { expiresInDays = 30, organizationIds = organizations }));
        return (created.GetProperty("id").GetGuid(), created.GetProperty("token").GetString()!);
    }

    /// <summary>Somebody with an account who belongs to this organization (or none).</summary>
    private async Task<HttpClient> PersonAsync(Fixture f, Guid? org = null, string role = "Viewer")
    {
        var (person, email, _) = await NewUserAsync(factory);
        if (org is { } id)
        {
            // Whoever owns that organization adds them: the owner for the two of the fixture.
            (await f.Owner.PostAsJsonAsync($"/api/organizations/{id}/members", new { email, role })).EnsureSuccessStatusCode();
        }
        return person;
    }

    private static Task<HttpResponseMessage> OpenAsync(HttpClient who, string token) => who.GetAsync($"/api/shared/dashboards/{token}");
    private static Task<HttpResponseMessage> RunAsync(HttpClient who, string token) =>
        who.PostAsJsonAsync($"/api/shared/dashboards/{token}/run", new { filters = new { } });

    [Fact]
    public async Task A_link_without_organizations_is_still_open_to_anyone_and_says_so_in_the_list()
    {
        var f = await NewFixtureAsync();
        var (_, token) = await LinkAsync(f);

        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(factory.CreateApiClient(), token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RunAsync(factory.CreateApiClient(), token)).StatusCode);
        var list = await ReadAsync(await f.Owner.GetAsync(Shares(f)));
        Assert.Equal(0, list[0].GetProperty("organizationIds").GetArrayLength());
    }

    [Fact]
    public async Task Nobody_signed_in_is_asked_to_sign_in_for_every_part_of_the_dashboard()
    {
        var f = await NewFixtureAsync();
        var (_, token) = await LinkAsync(f, f.Org);
        var anonymous = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await OpenAsync(anonymous, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RunAsync(anonymous, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/shared/dashboards/{token}/background")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/shared/dashboards/{token}/images/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task A_garbage_or_expired_sign_in_is_the_same_as_none()
    {
        var f = await NewFixtureAsync();
        var (_, token) = await LinkAsync(f, f.Org);
        var bad = factory.CreateApiClient();
        bad.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        Assert.Equal(HttpStatusCode.Unauthorized, (await OpenAsync(bad, token)).StatusCode);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Editor")]
    [InlineData("Owner")]
    public async Task A_member_of_a_chosen_organization_opens_it_whatever_their_role_and_sees_the_data(string role)
    {
        var f = await NewFixtureAsync();
        var (_, token) = await LinkAsync(f, f.Org);
        var member = await PersonAsync(f, f.Org, role);

        var page = await ReadAsync(await OpenAsync(member, token));
        var run = await ReadAsync(await RunAsync(member, token));

        Assert.Equal("Sales", page.GetProperty("name").GetString());
        Assert.Equal(1, run.GetProperty("widgets").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("widgets")[0].GetProperty("error").ValueKind);
        Assert.Contains("no-store", (await OpenAsync(member, token)).Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Somebody_signed_in_who_is_in_none_of_the_chosen_organizations_is_refused()
    {
        var f = await NewFixtureAsync();
        var (_, token) = await LinkAsync(f, f.Org);
        var stranger = await PersonAsync(f);
        var inOther = await PersonAsync(f, f.OtherOrg); // in an organization, but not a chosen one

        foreach (var person in new[] { stranger, inOther })
        {
            var open = await OpenAsync(person, token);
            Assert.Equal(HttpStatusCode.Forbidden, open.StatusCode);
            Assert.Contains("members of specific organizations", await open.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Forbidden, (await RunAsync(person, token)).StatusCode);
        }
    }

    [Fact]
    public async Task Any_one_of_several_chosen_organizations_is_enough()
    {
        var f = await NewFixtureAsync();
        var (_, token) = await LinkAsync(f, f.Org, f.OtherOrg);
        var onlyPartner = await PersonAsync(f, f.OtherOrg);
        var onlyHome = await PersonAsync(f, f.Org);

        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(onlyPartner, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(onlyHome, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await OpenAsync(factory.CreateApiClient(), token)).StatusCode);
    }

    [Fact]
    public async Task A_link_can_be_for_another_organization_than_the_dashboards_own()
    {
        var f = await NewFixtureAsync();
        var (_, token) = await LinkAsync(f, f.OtherOrg);

        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(await PersonAsync(f, f.OtherOrg), token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await OpenAsync(await PersonAsync(f, f.Org), token)).StatusCode);
    }

    [Fact]
    public async Task Leaving_the_organization_ends_access_with_the_very_same_link()
    {
        var f = await NewFixtureAsync();
        var (_, token) = await LinkAsync(f, f.Org);
        var (member, email, user) = await NewUserAsync(factory);
        (await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/members", new { email, role = "Viewer" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(member, token)).StatusCode);

        (await f.Owner.DeleteAsync($"/api/organizations/{f.Org}/members/{user.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await OpenAsync(member, token)).StatusCode);
    }

    [Fact]
    public async Task Wrong_links_are_the_same_404_for_everybody_and_never_say_who_a_link_was_for()
    {
        var f = await NewFixtureAsync();
        var (revokedId, revoked) = await LinkAsync(f, f.Org);
        (await f.Owner.DeleteAsync(Shares(f, $"/{revokedId}"))).EnsureSuccessStatusCode();
        var (expiredId, expired) = await LinkAsync(f, f.Org);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Entry(db.DashboardShares.Single(s => s.Id == expiredId)).Property(s => s.ExpiresUtc).CurrentValue = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }
        var never = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var member = await PersonAsync(f, f.Org);
        var stranger = await PersonAsync(f);

        foreach (var token in new[] { revoked, expired, never })
        foreach (var who in new[] { factory.CreateApiClient(), member, stranger })
        {
            var response = await OpenAsync(who, token);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("organizations", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task A_link_can_only_be_limited_to_organizations_its_maker_belongs_to()
    {
        var f = await NewFixtureAsync();
        var (_, _, _) = await NewUserAsync(factory);
        var outsiderOrg = await CreateOrgAsync((await NewUserAsync(factory)).Client, "Not mine");

        foreach (var organizations in new[]
        {
            new[] { outsiderOrg.Id },
            new[] { f.Org, outsiderOrg.Id },
            new[] { Guid.Empty },
            Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray(),
        })
        {
            var response = await f.Owner.PostAsJsonAsync(Shares(f), new { expiresInDays = 7, organizationIds = organizations });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("organizationids", (await response.Content.ReadAsStringAsync()).ToLowerInvariant());
        }
        Assert.Equal(0, (await ReadAsync(await f.Owner.GetAsync(Shares(f)))).GetArrayLength()); // nothing was made
    }

    [Fact]
    public async Task The_same_organization_twice_is_one_and_the_list_and_the_new_link_say_who_it_is_for()
    {
        var f = await NewFixtureAsync();

        var created = await ReadAsync(await f.Owner.PostAsJsonAsync(Shares(f),
            new { expiresInDays = 7, organizationIds = new[] { f.Org, f.Org, f.OtherOrg } }));

        Assert.Equal(new[] { f.Org, f.OtherOrg }.Order(), created.GetProperty("organizationIds").EnumerateArray().Select(x => x.GetGuid()).Order());
        var listed = (await ReadAsync(await f.Owner.GetAsync(Shares(f))))[0].GetProperty("organizationIds");
        Assert.Equal(2, listed.GetArrayLength());
    }

    [Fact]
    public async Task A_viewer_still_cannot_make_any_kind_of_link()
    {
        var f = await NewFixtureAsync();
        var viewer = await PersonAsync(f, f.Org);

        var response = await viewer.PostAsJsonAsync(Shares(f), new { expiresInDays = 7, organizationIds = new[] { f.Org } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task What_a_restricted_link_shows_is_what_a_public_one_shows_and_not_what_the_visitor_may_see_themselves()
    {
        // A viewer of the organization may not see the SQL or edit; the link does not widen that, and a member of another
        // organization with the same link sees exactly the same read-only page, with no query or dataset in it.
        var f = await NewFixtureAsync();
        var (_, token) = await LinkAsync(f, f.OtherOrg);
        var partner = await PersonAsync(f, f.OtherOrg);

        var text = await (await OpenAsync(partner, token)).Content.ReadAsStringAsync();

        Assert.DoesNotMatch(@"\bselect\b", text.ToLowerInvariant()); // not "legendSelector", which is a chart style
        Assert.DoesNotContain("datasetId", text);
        Assert.Equal(HttpStatusCode.NotFound, (await partner.GetAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}")).StatusCode);
    }
}
