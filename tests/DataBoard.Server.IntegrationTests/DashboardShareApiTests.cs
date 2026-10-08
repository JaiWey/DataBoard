using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataBoard.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>
/// Public links to a dashboard: who can make them, what a visitor with no account can and cannot see, and that every
/// wrong link looks the same. The data is the fixed "orders" table: Auckland 3 orders, Wellington 2, Christchurch 2, Dunedin 1.
/// </summary>
[Collection(ApiCollection.Name)]
public class DashboardShareApiTests(ApiFactory factory)
{
    private const string OrdersSql = "SELECT id, region, product, quantity, price, ordered_on FROM orders";

    private sealed record Fixture(HttpClient Owner, Guid Org, Guid Dashboard, Guid Chart, Guid FilterId);

    private async Task<Fixture> NewFixtureAsync(string filterColumn = "region", string filterOperator = "In")
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);

        var sourceResponse = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        sourceResponse.EnsureSuccessStatusCode();
        var source = (await sourceResponse.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id;

        var datasetResponse = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/datasets", new { dataSourceId = source, name = "Orders", sql = OrdersSql });
        var dataset = (await ReadAsync(datasetResponse)).GetProperty("id").GetGuid();

        var chartResponse = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/charts", new
        {
            name = "Revenue by region",
            datasetId = dataset,
            type = "Bar",
            definition = new
            {
                query = new
                {
                    dimensions = new[] { new { column = "region" } },
                    measures = new[] { new { aggregation = "Sum", column = "price", alias = "revenue" } },
                },
            },
        });
        var chart = (await ReadAsync(chartResponse)).GetProperty("id").GetGuid();

        var dashboardResponse = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/dashboards", new
        {
            name = "Sales",
            definition = new
            {
                widgets = new object[] { new { chartId = chart, layout = new { x = 0, y = 0, w = 6, h = 5 } } },
                filters = new object[] { new { label = "Region", column = filterColumn, @operator = filterOperator } },
            },
        });
        var dashboard = await ReadAsync(dashboardResponse);
        return new Fixture(owner, org.Id, dashboard.GetProperty("id").GetGuid(), chart,
            dashboard.GetProperty("definition").GetProperty("filters")[0].GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> CreateAsync(Fixture f, object? body = null, HttpClient? client = null) =>
        (client ?? f.Owner).PostAsJsonAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}/shares", body ?? new { expiresInDays = 30 });

    private static async Task<(Guid Id, string Token)> NewLinkAsync(Fixture f, object? body = null)
    {
        var created = await ReadAsync(await CreateAsync(f, body));
        return (created.GetProperty("id").GetGuid(), created.GetProperty("token").GetString()!);
    }

    private HttpClient Visitor() => factory.CreateApiClient(); // no account, no token

    private static Task<HttpResponseMessage> OpenAsync(HttpClient visitor, string token) =>
        visitor.GetAsync($"/api/shared/dashboards/{token}");

    private static Task<HttpResponseMessage> RunAsync(HttpClient visitor, string token, object? filters = null) =>
        visitor.PostAsJsonAsync($"/api/shared/dashboards/{token}/run", new { filters = filters ?? new { } });

    private async Task<HttpClient> AddMemberAsync(Fixture f, string role)
    {
        var (member, email, _) = await NewUserAsync(factory);
        (await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/members", new { email, role })).EnsureSuccessStatusCode();
        return member;
    }

    // ---- making and managing links ----------------------------------------------------------------------------

    [Fact]
    public async Task An_editor_makes_a_link_that_expires_and_the_list_gives_the_token_back_to_editors_only()
    {
        var f = await NewFixtureAsync();

        var before = DateTime.UtcNow;
        var created = await ReadAsync(await CreateAsync(f, new { expiresInDays = 7 }));

        var token = created.GetProperty("token").GetString()!;
        Assert.Equal(43, token.Length); // 256 bits as base64url
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
        Assert.InRange(created.GetProperty("expiresUtc").GetDateTime(), before.AddDays(7).AddMinutes(-1), DateTime.UtcNow.AddDays(7).AddMinutes(1));

        // An editor can copy the link again: the list carries the same secret.
        var list = await ReadAsync(await f.Owner.GetAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}/shares"));
        var listed = list.EnumerateArray().Single(s => s.GetProperty("id").GetString() == created.GetProperty("id").GetString());
        Assert.Equal(token, listed.GetProperty("token").GetString());

        // The link made from that copy opens the dashboard.
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(Visitor(), listed.GetProperty("token").GetString()!)).StatusCode);

        // Someone who cannot edit never gets the list, so never the secrets.
        var viewer = await AddMemberAsync(f, "Viewer");
        var refused = await viewer.GetAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}/shares");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.DoesNotContain(token, await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Two_links_are_never_the_same()
    {
        var f = await NewFixtureAsync();

        var first = await NewLinkAsync(f);
        var second = await NewLinkAsync(f);

        Assert.NotEqual(first.Token, second.Token);
    }

    [Fact]
    public async Task A_link_is_never_public_forever_unless_that_was_asked_for()
    {
        var f = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(f, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(f, new { expiresInDays = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(f, new { expiresInDays = 366 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(f, new { expiresInDays = -1 })).StatusCode);

        var forever = await ReadAsync(await CreateAsync(f, new { neverExpires = true }));
        Assert.Equal(JsonValueKind.Null, forever.GetProperty("expiresUtc").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(Visitor(), forever.GetProperty("token").GetString()!)).StatusCode);
    }

    [Fact]
    public async Task Only_the_hash_of_the_token_is_kept()
    {
        var f = await NewFixtureAsync();
        var (id, token) = await NewLinkAsync(f);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = db.DashboardShares.Single(s => s.Id == id).TokenHash;

        Assert.NotEqual(token, stored);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant(), stored);
    }

    [Fact]
    public async Task Viewers_cannot_make_list_or_revoke_links()
    {
        var f = await NewFixtureAsync();
        var (id, _) = await NewLinkAsync(f);
        var viewer = await AddMemberAsync(f, "Viewer");

        Assert.Equal(HttpStatusCode.Forbidden, (await CreateAsync(f, client: viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}/shares")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}/shares/{id}")).StatusCode);
    }

    [Fact]
    public async Task Someone_from_another_organization_cannot_touch_a_link()
    {
        var f = await NewFixtureAsync();
        var (id, token) = await NewLinkAsync(f);
        var (outsider, _, _) = await NewUserAsync(factory);

        var attempt = await outsider.DeleteAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}/shares/{id}");

        Assert.True(attempt.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(Visitor(), token)).StatusCode); // still works
    }

    [Fact]
    public async Task A_dashboard_has_at_most_ten_working_links()
    {
        var f = await NewFixtureAsync();
        for (var i = 0; i < 10; i++) await NewLinkAsync(f);

        var eleventh = await CreateAsync(f);

        Assert.Equal(HttpStatusCode.BadRequest, eleventh.StatusCode);
        Assert.Contains("at most 10", await eleventh.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Revoking_a_link_stops_it_at_once_and_leaves_the_others()
    {
        var f = await NewFixtureAsync();
        var (id, token) = await NewLinkAsync(f);
        var (_, other) = await NewLinkAsync(f);

        var revoked = await f.Owner.DeleteAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}/shares/{id}");

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await OpenAsync(Visitor(), token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RunAsync(Visitor(), token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(Visitor(), other)).StatusCode);
    }

    // ---- what a visitor sees ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_visitor_with_no_account_sees_the_dashboard_and_only_what_is_needed_to_draw_it()
    {
        var f = await NewFixtureAsync();
        var (_, token) = await NewLinkAsync(f);

        var response = await OpenAsync(Visitor(), token);
        var body = await response.Content.ReadAsStringAsync();
        var page = JsonDocument.Parse(body).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("Sales", page.GetProperty("name").GetString());
        Assert.Equal(6, page.GetProperty("widgets")[0].GetProperty("layout").GetProperty("w").GetInt32());
        Assert.Equal("Region", page.GetProperty("filters")[0].GetProperty("label").GetString());

        var chart = page.GetProperty("charts")[0];
        Assert.Equal("Revenue by region", chart.GetProperty("name").GetString());
        Assert.Equal("Bar", chart.GetProperty("type").GetString());
        Assert.Equal(1, chart.GetProperty("dimensionCount").GetInt32());

        // Nothing about where the data comes from: no queries, datasets, columns, SQL or organization.
        foreach (var secret in new[] { "query", "datasetId", "dataset", "column", "sql", "organizationId", "price", f.Org.ToString() })
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        // The table is called "orders": as a whole word (a style choice such as "barBorderStyle" happens to contain those letters).
        Assert.DoesNotMatch(@"\borders\b", body);
    }

    [Fact]
    public async Task A_visitor_runs_the_charts_with_the_shared_filters_and_never_gets_sql()
    {
        var f = await NewFixtureAsync();
        var (_, token) = await NewLinkAsync(f);

        var all = await ReadAsync(await RunAsync(Visitor(), token));
        var filtered = await ReadAsync(await RunAsync(Visitor(), token, new Dictionary<Guid, string[]> { [f.FilterId] = ["Auckland", "Dunedin"] }));

        string[] Regions(JsonElement run) => run.GetProperty("widgets")[0].GetProperty("result").GetProperty("rows").EnumerateArray().Select(r => r[0].GetString()!).ToArray();
        Assert.Equal(["Auckland", "Christchurch", "Dunedin", "Wellington"], Regions(all));
        Assert.Equal(["Auckland", "Dunedin"], Regions(filtered));
        Assert.Equal(JsonValueKind.Null, filtered.GetProperty("widgets")[0].GetProperty("result").GetProperty("sql").ValueKind);
    }

    [Fact]
    public async Task A_visitor_does_not_see_table_or_column_names_when_a_chart_fails()
    {
        var f = await NewFixtureAsync(filterColumn: "quantity", filterOperator: "Gte");
        var (_, token) = await NewLinkAsync(f);

        var member = await ReadAsync(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}/run",
            new { filters = new Dictionary<Guid, string[]> { [f.FilterId] = ["lots"] } }));
        var visitor = await ReadAsync(await RunAsync(Visitor(), token, new Dictionary<Guid, string[]> { [f.FilterId] = ["lots"] }));

        Assert.Contains("quantity", member.GetProperty("widgets")[0].GetProperty("error").GetString()); // a member is told what is wrong
        Assert.Equal("This chart could not be shown.", visitor.GetProperty("widgets")[0].GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_link_follows_changes_to_the_dashboard_and_forgets_deleted_charts()
    {
        var f = await NewFixtureAsync();
        var (_, token) = await NewLinkAsync(f);

        (await f.Owner.DeleteAsync($"/api/organizations/{f.Org}/charts/{f.Chart}")).EnsureSuccessStatusCode();

        var page = await ReadAsync(await OpenAsync(Visitor(), token));
        Assert.Empty(page.GetProperty("charts").EnumerateArray());
        Assert.Empty(page.GetProperty("widgets").EnumerateArray());
    }

    // ---- wrong links all look the same ------------------------------------------------------------------------

    [Fact]
    public async Task Every_kind_of_wrong_link_gets_the_same_answer()
    {
        var f = await NewFixtureAsync();
        var (revokedId, revoked) = await NewLinkAsync(f);
        (await f.Owner.DeleteAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}/shares/{revokedId}")).EnsureSuccessStatusCode();

        var (expiredId, expired) = await NewLinkAsync(f, new { expiresInDays = 1 });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var share = db.DashboardShares.Single(s => s.Id == expiredId);
            db.Entry(share).Property(s => s.ExpiresUtc).CurrentValue = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }

        var (_, ofDeletedDashboard) = await NewLinkAsync(f);
        // The dashboard (and with it its links) goes away.
        (await f.Owner.DeleteAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}")).EnsureSuccessStatusCode();

        var neverIssued = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var wrong = new[] { neverIssued, revoked, expired, ofDeletedDashboard, "short", new string('a', 500), "has spaces in it................................", "x" };

        var answers = new List<(HttpStatusCode, string)>();
        foreach (var token in wrong)
        {
            var view = await OpenAsync(Visitor(), Uri.EscapeDataString(token));
            var run = await RunAsync(Visitor(), Uri.EscapeDataString(token));
            Assert.Equal(HttpStatusCode.NotFound, view.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, run.StatusCode);
            // Only the id of the request differs from one answer to the next.
            answers.Add((view.StatusCode, System.Text.RegularExpressions.Regex.Replace(await view.Content.ReadAsStringAsync(), "\"traceId\":\"[^\"]*\"", "")));
        }

        // The body names no reason, so it tells a guesser nothing about which links exist.
        Assert.Single(answers.Select(a => a.Item2).Distinct());
        Assert.Contains("not valid or has expired", answers[0].Item2);
    }

    [Fact]
    public async Task A_link_opens_nothing_else()
    {
        var f = await NewFixtureAsync();
        var (_, token) = await NewLinkAsync(f);
        var visitor = Visitor();

        // Not as a token for the member API ...
        visitor.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync($"/api/organizations/{f.Org}/dashboards")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}")).StatusCode);

        // ... and with no token at all.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Visitor().GetAsync($"/api/organizations/{f.Org}/charts")).StatusCode);
    }
}
