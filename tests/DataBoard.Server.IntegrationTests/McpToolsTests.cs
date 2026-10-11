using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DataBoard.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>
/// An AI assistant's view: the tools are called through a real MCP client over HTTP, with a personal access token. The
/// data is the fixed "orders" table: Auckland 3 orders (revenue 2480), Wellington 2, Christchurch 2, Dunedin 1.
/// </summary>
[Collection(ApiCollection.Name)]
public class McpToolsTests(ApiFactory factory)
{
    private const string OrdersSql = "SELECT id, region, product, quantity, price, ordered_on FROM orders";

    private sealed record Setup(HttpClient Owner, Guid Org, Guid Dataset);

    private async Task<Setup> NewSetupAsync()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var source = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        source.EnsureSuccessStatusCode();
        var sourceId = (await source.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id;
        var dataset = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/datasets", new { dataSourceId = sourceId, name = "Orders", sql = OrdersSql });
        return new Setup(owner, org.Id, (await Read(dataset)).GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<string> TokenAsync(HttpClient signedIn, string scope = "Write", int? days = 30)
    {
        var response = await signedIn.PostAsJsonAsync("/api/me/tokens", new { name = "Test", scope, expiresInDays = days });
        return (await Read(response)).GetProperty("token").GetString()!;
    }

    private async Task<McpClient> ConnectAsync(string? token)
    {
        var http = factory.CreateApiClient();
        if (token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("https://localhost/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static async Task<CallToolResult> CallAsync(McpClient client, string tool, object arguments) =>
        await client.CallToolAsync(tool, ((Dictionary<string, object?>)ToDictionary(arguments)));

    private static Dictionary<string, object?> ToDictionary(object arguments) =>
        arguments.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(arguments));

    private static string TextOf(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static JsonElement Json_(CallToolResult result)
    {
        Assert.True(result.IsError != true, TextOf(result));
        return JsonDocument.Parse(TextOf(result)).RootElement.Clone();
    }

    private static readonly object RevenueByRegion = new
    {
        dimensions = new[] { new { column = "region" } },
        measures = new[] { new { aggregation = "Sum", column = "price", alias = "revenue" } },
    };

    // ---- what the assistant is offered -------------------------------------------------------------------------

    [Fact]
    public async Task The_assistant_is_told_what_the_tools_are_for_and_none_of_them_can_delete_or_write_sql()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        await using var client = await ConnectAsync(await TokenAsync(owner));

        var tools = await client.ListToolsAsync();

        Assert.Equal(
            ["add_charts_to_dashboard", "add_dashboard_filter", "create_chart", "create_dashboard", "get_dashboard", "list_charts",
             "list_dashboards", "list_datasets", "list_organizations", "preview_chart", "set_widget_layout", "update_chart"],
            tools.Select(t => t.Name).Order());
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Name));
        Assert.DoesNotContain(tools, t => t.Name.Contains("delete", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("sql", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("preview_chart", client.ServerInstructions);
        Assert.Equal("DataBoard", client.ServerInfo.Name);
    }

    [Fact]
    public async Task The_query_format_is_described_to_the_assistant_in_the_tool_schema()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        await using var client = await ConnectAsync(await TokenAsync(owner));

        var create = (await client.ListToolsAsync()).Single(t => t.Name == "create_chart");
        var schema = create.JsonSchema.GetRawText();

        foreach (var needed in new[] { "organizationId", "datasetId", "query", "type", "Sum", "CountDistinct", "seriesLimit", "BarHorizontal" })
            Assert.Contains(needed, schema);
    }

    [Fact]
    public async Task Without_a_token_the_endpoint_cannot_be_used()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () => { await using var client = await ConnectAsync(null); });
    }

    // ---- looking around ----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_assistant_finds_the_organizations_and_datasets_but_never_sees_the_sql()
    {
        var s = await NewSetupAsync();
        await using var client = await ConnectAsync(await TokenAsync(s.Owner, "Read"));

        var orgs = Json_(await CallAsync(client, "list_organizations", new { }));
        var datasets = await CallAsync(client, "list_datasets", new { organizationId = s.Org });

        // Everyone also belongs to the default organization, which comes first.
        var mine = orgs.EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == s.Org);
        Assert.Equal("Owner", mine.GetProperty("role").GetString());
        Assert.True(orgs[0].GetProperty("isDefault").GetBoolean());
        var orders = Json_(datasets)[0];
        Assert.Equal("Orders", orders.GetProperty("name").GetString());
        var price = orders.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "price");
        Assert.Equal("Measure", price.GetProperty("role").GetString());
        Assert.DoesNotContain("SELECT", TextOf(datasets), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", TextOf(datasets), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_preview_returns_the_rows_without_saving_anything_or_showing_sql()
    {
        var s = await NewSetupAsync();
        await using var client = await ConnectAsync(await TokenAsync(s.Owner, "Read"));

        var result = await CallAsync(client, "preview_chart", new { organizationId = s.Org, datasetId = s.Dataset, query = RevenueByRegion });

        var preview = Json_(result);
        Assert.Equal(["region", "revenue"], preview.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        var rows = preview.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal("Auckland", rows[0][0].GetString());
        Assert.Equal(2480m, rows[0][1].GetDecimal());
        Assert.DoesNotContain("SELECT", TextOf(result), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("[]", await s.Owner.GetStringAsync($"/api/organizations/{s.Org}/charts"));
    }

    [Fact]
    public async Task A_preview_can_be_asked_for_fewer_rows()
    {
        var s = await NewSetupAsync();
        await using var client = await ConnectAsync(await TokenAsync(s.Owner, "Read"));

        var preview = Json_(await CallAsync(client, "preview_chart", new { organizationId = s.Org, datasetId = s.Dataset, query = RevenueByRegion, maxRows = 2 }));

        Assert.Equal(2, preview.GetProperty("rows").GetArrayLength());
        Assert.Equal(4, preview.GetProperty("rowCount").GetInt32());
    }

    [Fact]
    public async Task A_wrong_query_comes_back_as_an_error_that_says_what_to_change()
    {
        var s = await NewSetupAsync();
        await using var client = await ConnectAsync(await TokenAsync(s.Owner, "Read"));

        var result = await CallAsync(client, "preview_chart", new
        {
            organizationId = s.Org,
            datasetId = s.Dataset,
            query = new { dimensions = new[] { new { column = "country" } }, measures = new[] { new { aggregation = "Sum", column = "region" } } },
        });

        Assert.True(result.IsError);
        Assert.Contains("'country' is not a column of this dataset", TextOf(result));
        Assert.Contains("cannot be applied to 'region'", TextOf(result)); // every problem at once
    }

    // ---- making things ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dashboard_is_made_from_a_prompt_the_way_an_assistant_would_do_it()
    {
        var s = await NewSetupAsync();
        await using var client = await ConnectAsync(await TokenAsync(s.Owner));

        var chart = Json_(await CallAsync(client, "create_chart", new
        {
            organizationId = s.Org,
            name = "Revenue by region",
            datasetId = s.Dataset,
            type = "Bar",
            query = RevenueByRegion,
            description = "Made by an assistant",
        }));
        var chartId = chart.GetProperty("id").GetGuid();
        var orders = Json_(await CallAsync(client, "create_chart", new
        {
            organizationId = s.Org,
            name = "Orders",
            datasetId = s.Dataset,
            type = "Number",
            query = new { measures = new[] { new { aggregation = "Count", alias = "n" } } },
        }));

        var dashboard = Json_(await CallAsync(client, "create_dashboard", new
        {
            organizationId = s.Org,
            name = "Sales",
            chartIds = new[] { chartId, orders.GetProperty("id").GetGuid() },
        }));
        var dashboardId = dashboard.GetProperty("id").GetGuid();
        await CallAsync(client, "set_widget_layout", new { organizationId = s.Org, dashboardId, chartId, x = 0, y = 0, w = 8, h = 6 });
        await CallAsync(client, "add_dashboard_filter", new { organizationId = s.Org, dashboardId, label = "Region", column = "region", @operator = "In" });

        // What the person finds when they open DataBoard: the same chart and dashboard as if they had built them by hand.
        var saved = await Read(await s.Owner.GetAsync($"/api/organizations/{s.Org}/charts/{chartId}"));
        Assert.Equal("Bar", saved.GetProperty("type").GetString());
        Assert.Equal("Sum", saved.GetProperty("definition").GetProperty("query").GetProperty("measures")[0].GetProperty("aggregation").GetString());

        var fetched = Json_(await CallAsync(client, "get_dashboard", new { organizationId = s.Org, dashboardId }));
        Assert.Equal(2, fetched.GetProperty("widgets").GetArrayLength());
        Assert.Equal(8, fetched.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("chartId").GetGuid() == chartId).GetProperty("layout").GetProperty("w").GetInt32());
        Assert.Equal("Region", fetched.GetProperty("filters")[0].GetProperty("label").GetString());

        var run = await Read(await s.Owner.PostAsJsonAsync($"/api/organizations/{s.Org}/dashboards/{dashboardId}/run", new { filters = new { } }));
        Assert.Equal(2, run.GetProperty("widgets").GetArrayLength());
        Assert.All(run.GetProperty("widgets").EnumerateArray(), w => Assert.Equal(JsonValueKind.Null, w.GetProperty("error").ValueKind));
    }

    [Fact]
    public async Task Charts_can_be_added_to_a_dashboard_later_without_repeating_one_and_changed_in_place()
    {
        var s = await NewSetupAsync();
        await using var client = await ConnectAsync(await TokenAsync(s.Owner));
        var first = Json_(await CallAsync(client, "create_chart", new { organizationId = s.Org, name = "A", datasetId = s.Dataset, type = "Table", query = RevenueByRegion })).GetProperty("id").GetGuid();
        var second = Json_(await CallAsync(client, "create_chart", new { organizationId = s.Org, name = "B", datasetId = s.Dataset, type = "Bar", query = RevenueByRegion })).GetProperty("id").GetGuid();
        var dashboardId = Json_(await CallAsync(client, "create_dashboard", new { organizationId = s.Org, name = "D", chartIds = new[] { first } })).GetProperty("id").GetGuid();

        var added = Json_(await CallAsync(client, "add_charts_to_dashboard", new { organizationId = s.Org, dashboardId, chartIds = new[] { first, second } }));
        var updated = Json_(await CallAsync(client, "update_chart", new
        {
            organizationId = s.Org, chartId = second, name = "B (donut)", datasetId = s.Dataset, type = "Donut", query = RevenueByRegion,
        }));

        Assert.Equal([first, second], added.GetProperty("widgets").EnumerateArray().Select(w => w.GetProperty("chartId").GetGuid()));
        Assert.Equal("Donut", updated.GetProperty("type").GetString());
        Assert.Equal("B (donut)", (await Read(await s.Owner.GetAsync($"/api/organizations/{s.Org}/charts/{second}"))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_series_limit_and_stacking_can_be_asked_for()
    {
        var s = await NewSetupAsync();
        await using var client = await ConnectAsync(await TokenAsync(s.Owner));

        var chart = Json_(await CallAsync(client, "create_chart", new
        {
            organizationId = s.Org,
            name = "Stacked",
            datasetId = s.Dataset,
            type = "Line",
            query = new
            {
                dimensions = new object[] { new { column = "ordered_on", grain = "Month" }, new { column = "region" } },
                measures = new[] { new { aggregation = "Sum", column = "price", alias = "revenue" } },
                seriesLimit = 2,
            },
            style = new { legend = true, labels = false, palette = "warm", stack = true },
        }));

        Assert.Equal(2, chart.GetProperty("query").GetProperty("seriesLimit").GetInt32());
        Assert.True(chart.GetProperty("style").GetProperty("stack").GetBoolean());
        Assert.Equal("warm", chart.GetProperty("style").GetProperty("palette").GetString());
    }

    [Fact]
    public async Task A_bad_chart_is_refused_with_the_reason_and_nothing_is_saved()
    {
        var s = await NewSetupAsync();
        await using var client = await ConnectAsync(await TokenAsync(s.Owner));

        var result = await CallAsync(client, "create_chart", new
        {
            organizationId = s.Org, name = "Broken", datasetId = s.Dataset, type = "Bar",
            query = new { dimensions = new[] { new { column = "nope" } }, measures = new[] { new { aggregation = "Count" } } },
        });
        var wrongDashboard = await CallAsync(client, "create_dashboard", new { organizationId = s.Org, name = "X", chartIds = new[] { Guid.NewGuid() } });

        Assert.True(result.IsError);
        Assert.Contains("'nope' is not a column", TextOf(result));
        Assert.True(wrongDashboard.IsError);
        Assert.Contains("chart was not found", TextOf(wrongDashboard));
        Assert.Equal("[]", await s.Owner.GetStringAsync($"/api/organizations/{s.Org}/charts"));
        Assert.Equal("[]", await s.Owner.GetStringAsync($"/api/organizations/{s.Org}/dashboards"));
    }

    // ---- what a token cannot do -------------------------------------------------------------------------------

    [Fact]
    public async Task A_read_only_token_cannot_change_anything()
    {
        var s = await NewSetupAsync();
        await using var client = await ConnectAsync(await TokenAsync(s.Owner, "Read"));

        var create = await CallAsync(client, "create_chart", new { organizationId = s.Org, name = "X", datasetId = s.Dataset, type = "Table", query = RevenueByRegion });
        var dashboard = await CallAsync(client, "create_dashboard", new { organizationId = s.Org, name = "X", chartIds = Array.Empty<Guid>() });

        Assert.True(create.IsError);
        Assert.Contains("read-only", TextOf(create));
        Assert.True(dashboard.IsError);
        Assert.Contains("read-only", TextOf(dashboard));
        Assert.Equal("[]", await s.Owner.GetStringAsync($"/api/organizations/{s.Org}/charts"));
    }

    [Fact]
    public async Task A_token_never_does_more_than_its_owner_may()
    {
        var s = await NewSetupAsync();
        var (viewer, email, _) = await NewUserAsync(factory);
        (await s.Owner.PostAsJsonAsync($"/api/organizations/{s.Org}/members", new { email, role = "Viewer" })).EnsureSuccessStatusCode();
        await using var client = await ConnectAsync(await TokenAsync(viewer, "Write")); // the widest token a viewer can have

        var look = await CallAsync(client, "list_datasets", new { organizationId = s.Org });
        var create = await CallAsync(client, "create_chart", new { organizationId = s.Org, name = "X", datasetId = s.Dataset, type = "Table", query = RevenueByRegion });

        Assert.True(look.IsError != true); // a viewer may look
        Assert.True(create.IsError);
        Assert.Contains("Editor", TextOf(create));
        Assert.Equal("[]", await s.Owner.GetStringAsync($"/api/organizations/{s.Org}/charts"));
    }

    [Fact]
    public async Task A_token_sees_nothing_of_organizations_its_owner_does_not_belong_to()
    {
        var s = await NewSetupAsync();
        var (outsider, _, _) = await NewUserAsync(factory);
        await using var client = await ConnectAsync(await TokenAsync(outsider));

        var orgs = Json_(await CallAsync(client, "list_organizations", new { }));
        var datasets = await CallAsync(client, "list_datasets", new { organizationId = s.Org });

        // Only the default organization every user belongs to, never the owner's.
        Assert.All(orgs.EnumerateArray(), o => Assert.True(o.GetProperty("isDefault").GetBoolean()));
        Assert.True(datasets.IsError);
        Assert.Contains("Organization not found", TextOf(datasets)); // not "forbidden": it does not even say it exists
    }

    [Fact]
    public async Task A_revoked_or_expired_token_stops_working()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var revoked = await Read(await owner.PostAsJsonAsync("/api/me/tokens", new { name = "old", scope = "Read", expiresInDays = 30 }));
        var expired = await Read(await owner.PostAsJsonAsync("/api/me/tokens", new { name = "stale", scope = "Read", expiresInDays = 1 }));
        await using (var works = await ConnectAsync(revoked.GetProperty("token").GetString()))
            Assert.NotNull(await works.ListToolsAsync());

        (await owner.DeleteAsync($"/api/me/tokens/{revoked.GetProperty("id").GetGuid()}")).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var token = db.ApiTokens.Single(t => t.Id == expired.GetProperty("id").GetGuid());
            db.Entry(token).Property(t => t.ExpiresUtc).CurrentValue = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAnyAsync<Exception>(async () => { await using var c = await ConnectAsync(revoked.GetProperty("token").GetString()); });
        await Assert.ThrowsAnyAsync<Exception>(async () => { await using var c = await ConnectAsync(expired.GetProperty("token").GetString()); });
    }
}
