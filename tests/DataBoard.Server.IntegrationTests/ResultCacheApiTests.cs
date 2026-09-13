using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>
/// Result caching against a real database. Each test makes its own small table and changes it behind the API's back,
/// so a stale answer and a fresh one can be told apart: the table holds one row whose number is 1, and "grow" adds 10.
/// </summary>
[Collection(ApiCollection.Name)]
public class ResultCacheApiTests(ApiFactory factory)
{
    private sealed record Fixture(HttpClient Owner, Guid Org, Guid Dataset, Guid Chart, Guid Dashboard, string Table)
    {
        public string DashboardRunUrl => $"/api/organizations/{Org}/dashboards/{Dashboard}/run";
    }

    private async Task<Fixture> NewFixtureAsync(int? cacheSeconds = null)
    {
        var table = "cache_probe_" + Guid.NewGuid().ToString("N");
        await factory.ExecuteAsOwnerAsync($"CREATE TABLE {table} (label text, n int); INSERT INTO {table} VALUES ('a', 1); GRANT SELECT ON {table} TO reader;");

        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var source = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Probe DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        var sourceId = (await source.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id;

        var dataset = (await ReadAsync(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/datasets",
            new { dataSourceId = sourceId, name = "Probe", sql = $"SELECT label, n FROM {table}" }))).GetProperty("id").GetGuid();
        var chart = (await ReadAsync(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/charts", new
        {
            name = "Total",
            datasetId = dataset,
            type = "Number",
            definition = new { query = new { measures = new[] { new { aggregation = "Sum", column = "n", alias = "total" } } } },
        }))).GetProperty("id").GetGuid();
        var dashboard = (await ReadAsync(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/dashboards",
            new { name = "Probe board", definition = new { widgets = new object[] { new { chartId = chart } }, filters = Array.Empty<object>() } }))).GetProperty("id").GetGuid();

        var f = new Fixture(owner, org.Id, dataset, chart, dashboard, table);
        if (cacheSeconds is { } seconds) await SetCacheAsync(f, seconds);
        return f;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> SetCacheRaw(Fixture f, int seconds, HttpClient? client = null) =>
        (client ?? f.Owner).PutAsJsonAsync($"/api/organizations/{f.Org}/datasets/{f.Dataset}/cache", new { cacheSeconds = seconds });

    private static async Task SetCacheAsync(Fixture f, int seconds) => await ReadAsync(await SetCacheRaw(f, seconds));

    private Task Grow(Fixture f) => factory.ExecuteAsOwnerAsync($"UPDATE {f.Table} SET n = n + 10");

    /// <summary>What a dashboard run shows for its one chart: the number, and whether it came from the cache.</summary>
    private static async Task<(long Total, bool Cached, JsonElement Widget)> RunBoardAsync(HttpClient client, string url, object? body = null)
    {
        var run = await ReadAsync(await client.PostAsJsonAsync(url, body ?? new { filters = new { } }));
        var widget = run.GetProperty("widgets")[0];
        var result = widget.GetProperty("result");
        return (result.GetProperty("rows")[0][0].GetInt64(), result.GetProperty("cachedUtc").ValueKind != JsonValueKind.Null, widget);
    }

    private async Task<HttpClient> AddMemberAsync(Fixture f, string role)
    {
        var (member, email, _) = await NewUserAsync(factory);
        (await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/members", new { email, role })).EnsureSuccessStatusCode();
        return member;
    }

    // ---- turning it on -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Datasets_do_not_keep_results_until_an_editor_says_so()
    {
        var f = await NewFixtureAsync();

        var dataset = await ReadAsync(await f.Owner.GetAsync($"/api/organizations/{f.Org}/datasets/{f.Dataset}"));
        Assert.Equal(0, dataset.GetProperty("cacheSeconds").GetInt32());

        Assert.Equal(1, (await RunBoardAsync(f.Owner, f.DashboardRunUrl)).Total);
        await Grow(f);
        var again = await RunBoardAsync(f.Owner, f.DashboardRunUrl);
        Assert.Equal(11, again.Total); // live every time
        Assert.False(again.Cached);
    }

    [Fact]
    public async Task An_editor_chooses_how_long_results_are_kept()
    {
        var f = await NewFixtureAsync();

        var set = await ReadAsync(await SetCacheRaw(f, 300));

        Assert.Equal(300, set.GetProperty("cacheSeconds").GetInt32());
        var list = await ReadAsync(await f.Owner.GetAsync($"/api/organizations/{f.Org}/datasets"));
        Assert.Equal(300, list[0].GetProperty("cacheSeconds").GetInt32());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(86_401)]
    public async Task How_long_results_are_kept_has_limits(int seconds)
    {
        var f = await NewFixtureAsync();

        var response = await SetCacheRaw(f, seconds);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("0 to 86400", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Only_editors_choose_it()
    {
        var f = await NewFixtureAsync();
        var viewer = await AddMemberAsync(f, "Viewer");
        var (stranger, _, _) = await NewUserAsync(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await SetCacheRaw(f, 60, viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetCacheRaw(f, 60, stranger)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetCacheRaw(f, 60, factory.CreateApiClient())).StatusCode);
    }

    // ---- using it ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dashboard_serves_the_stored_answer_until_it_is_refreshed()
    {
        var f = await NewFixtureAsync(cacheSeconds: 300);

        var first = await RunBoardAsync(f.Owner, f.DashboardRunUrl);
        await Grow(f);
        var second = await RunBoardAsync(f.Owner, f.DashboardRunUrl);

        Assert.Equal((1, false), (first.Total, first.Cached)); // fetched just now
        Assert.Equal((1, true), (second.Total, second.Cached)); // the database says 11, the stored answer says 1
        Assert.NotEqual(JsonValueKind.Null, second.Widget.GetProperty("result").GetProperty("cachedUtc").ValueKind);
    }

    [Fact]
    public async Task A_saved_chart_uses_the_stored_answer_too()
    {
        var f = await NewFixtureAsync(cacheSeconds: 300);
        string RunUrl() => $"/api/organizations/{f.Org}/charts/{f.Chart}/run";
        async Task<(long, bool)> Run()
        {
            var result = await ReadAsync(await f.Owner.PostAsync(RunUrl(), null));
            return (result.GetProperty("rows")[0][0].GetInt64(), result.GetProperty("cachedUtc").ValueKind != JsonValueKind.Null);
        }

        Assert.Equal((1L, false), await Run());
        await Grow(f);
        Assert.Equal((1L, true), await Run());
    }

    [Fact]
    public async Task The_chart_editors_preview_is_always_live_and_does_not_disturb_what_is_stored()
    {
        var f = await NewFixtureAsync(cacheSeconds: 300);
        await RunBoardAsync(f.Owner, f.DashboardRunUrl); // stores 1
        await Grow(f);

        var preview = await ReadAsync(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/datasets/{f.Dataset}/query",
            new { measures = new[] { new { aggregation = "Sum", column = "n", alias = "total" } } }));

        Assert.Equal(11, preview.GetProperty("rows")[0][0].GetInt64());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("cachedUtc").ValueKind);
        Assert.Equal(1, (await RunBoardAsync(f.Owner, f.DashboardRunUrl)).Total); // the dashboard still has its stored answer
    }

    [Fact]
    public async Task An_editor_can_refresh_and_the_new_answer_is_what_everyone_gets_next()
    {
        var f = await NewFixtureAsync(cacheSeconds: 300);
        await RunBoardAsync(f.Owner, f.DashboardRunUrl);
        await Grow(f);

        var refreshed = await RunBoardAsync(f.Owner, f.DashboardRunUrl, new { filters = new { }, refresh = true });
        await Grow(f); // and again, so the next run cannot be a live one by accident
        var next = await RunBoardAsync(f.Owner, f.DashboardRunUrl);

        Assert.Equal((11, false), (refreshed.Total, refreshed.Cached));
        Assert.Equal((11, true), (next.Total, next.Cached)); // 11, not 21
    }

    [Fact]
    public async Task Viewers_see_the_stored_answer_without_the_sql_and_cannot_force_a_refresh()
    {
        var f = await NewFixtureAsync(cacheSeconds: 300);
        var viewer = await AddMemberAsync(f, "Viewer");
        var stored = await RunBoardAsync(f.Owner, f.DashboardRunUrl); // an editor's run stores it, SQL and all
        Assert.NotEqual(JsonValueKind.Null, stored.Widget.GetProperty("result").GetProperty("sql").ValueKind);
        await Grow(f);

        var seen = await RunBoardAsync(viewer, f.DashboardRunUrl);
        var forced = await viewer.PostAsJsonAsync(f.DashboardRunUrl, new { filters = new { }, refresh = true });

        Assert.Equal((1, true), (seen.Total, seen.Cached));
        Assert.Equal(JsonValueKind.Null, seen.Widget.GetProperty("result").GetProperty("sql").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, forced.StatusCode);
        Assert.Equal(1, (await RunBoardAsync(viewer, f.DashboardRunUrl)).Total); // and the refusal changed nothing
    }

    [Fact]
    public async Task Editing_the_dataset_gives_a_fresh_answer()
    {
        var f = await NewFixtureAsync(cacheSeconds: 300);
        await RunBoardAsync(f.Owner, f.DashboardRunUrl);
        await Grow(f);

        var update = await f.Owner.PutAsJsonAsync($"/api/organizations/{f.Org}/datasets/{f.Dataset}",
            new { name = "Probe", sql = $"SELECT label, n FROM {f.Table} WHERE n > 0" });
        update.EnsureSuccessStatusCode();
        var after = await RunBoardAsync(f.Owner, f.DashboardRunUrl);

        Assert.Equal((11, false), (after.Total, after.Cached));
    }

    [Fact]
    public async Task Changing_how_long_results_are_kept_shows_fresh_data_at_once()
    {
        var f = await NewFixtureAsync(cacheSeconds: 300);
        await RunBoardAsync(f.Owner, f.DashboardRunUrl);
        await Grow(f);

        await SetCacheAsync(f, 600);
        var after = await RunBoardAsync(f.Owner, f.DashboardRunUrl);

        Assert.Equal((11, false), (after.Total, after.Cached));
    }

    [Fact]
    public async Task Turning_it_off_again_shows_live_data_at_once_and_keeps_nothing()
    {
        var f = await NewFixtureAsync(cacheSeconds: 300);
        await RunBoardAsync(f.Owner, f.DashboardRunUrl); // stores 1
        await Grow(f);

        await SetCacheAsync(f, 0);
        var off = await RunBoardAsync(f.Owner, f.DashboardRunUrl);
        await Grow(f);
        var stillLive = await RunBoardAsync(f.Owner, f.DashboardRunUrl);

        Assert.Equal((11, false), (off.Total, off.Cached)); // not the stored 1
        Assert.Equal((21, false), (stillLive.Total, stillLive.Cached));
    }
    [Fact]
    public async Task Different_parameter_values_are_different_questions_and_the_same_values_are_the_same_question()
    {
        var f = await NewFixtureAsync(cacheSeconds: 300);
        (await f.Owner.PutAsJsonAsync($"/api/organizations/{f.Org}/datasets/{f.Dataset}", new
        {
            name = "Probe",
            sql = $"SELECT label, n + {{{{plus}}}} AS n FROM {f.Table}",
            parameters = new object[] { new { name = "plus", type = "Number", @default = 0 } },
        })).EnsureSuccessStatusCode();
        await SetCacheAsync(f, 300); // an update keeps the setting, but say it plainly

        // Each chart is its own chart; what is stored is an answer to a question, whichever chart asked it.
        async Task<(long Total, bool Cached)> Run(string name, int plus)
        {
            var chart = (await ReadAsync(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/charts", new
            {
                name, datasetId = f.Dataset, type = "Number",
                definition = new { query = new { measures = new[] { new { aggregation = "Sum", column = "n", alias = "total" } }, parameters = new { plus } } },
            }))).GetProperty("id").GetGuid();
            var run = await ReadAsync(await f.Owner.PostAsync($"/api/organizations/{f.Org}/charts/{chart}/run", null));
            return (run.GetProperty("rows")[0][0].GetInt64(), run.GetProperty("cachedUtc").ValueKind != JsonValueKind.Null);
        }

        Assert.Equal((1L, false), await Run("plus 0", 0));
        Assert.Equal((101L, false), await Run("plus 100", 100));
        await Grow(f); // the table now says 11
        Assert.Equal((1L, true), await Run("plus 0 again", 0)); // the same question as the first: its stored answer
        Assert.Equal((101L, true), await Run("plus 100 again", 100));
        Assert.Equal((211L, false), await Run("plus 200", 200)); // a question nobody asked yet: live
    }

    // ---- public links ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_visitor_with_a_share_link_gets_the_stored_answer_and_cannot_force_a_refresh()
    {
        var f = await NewFixtureAsync(cacheSeconds: 300);
        var link = await ReadAsync(await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/dashboards/{f.Dashboard}/shares", new { expiresInDays = 1 }));
        var url = $"/api/shared/dashboards/{link.GetProperty("token").GetString()}/run";
        var visitor = factory.CreateApiClient();
        Assert.Equal(1, (await RunBoardAsync(visitor, url)).Total); // the first visitor stores it
        await Grow(f);

        var second = await RunBoardAsync(visitor, url);
        var forced = await RunBoardAsync(visitor, url, new { filters = new { }, refresh = true });

        Assert.Equal((1, true), (second.Total, second.Cached));
        Assert.Equal((1, true), (forced.Total, forced.Cached)); // the flag means nothing to a visitor
    }

    [Fact]
    public async Task Another_organizations_dataset_never_shares_an_answer_even_with_the_same_sql()
    {
        var one = await NewFixtureAsync(cacheSeconds: 300);
        await RunBoardAsync(one.Owner, one.DashboardRunUrl); // stores 1 for the first organization
        await Grow(one);

        // A second organization reads the very same table, with the very same SQL.
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var source = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Probe DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        var sourceId = (await source.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id;
        var dataset = (await ReadAsync(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/datasets",
            new { dataSourceId = sourceId, name = "Probe", sql = $"SELECT label, n FROM {one.Table}" }))).GetProperty("id").GetGuid();
        await ReadAsync(await owner.PutAsJsonAsync($"/api/organizations/{org.Id}/datasets/{dataset}/cache", new { cacheSeconds = 300 }));
        var chart = (await ReadAsync(await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/charts", new
        {
            name = "Total", datasetId = dataset, type = "Number",
            definition = new { query = new { measures = new[] { new { aggregation = "Sum", column = "n", alias = "total" } } } },
        }))).GetProperty("id").GetGuid();

        var run = await ReadAsync(await owner.PostAsync($"/api/organizations/{org.Id}/charts/{chart}/run", null));

        Assert.Equal(11, run.GetProperty("rows")[0][0].GetInt64()); // its own, live answer, not the first organization's 1
        Assert.Equal(JsonValueKind.Null, run.GetProperty("cachedUtc").ValueKind);
    }
}
