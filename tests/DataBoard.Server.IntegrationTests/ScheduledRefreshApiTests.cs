using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataBoard.Application.Abstractions;
using DataBoard.Application.Dashboards;
using DataBoard.Server.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>
/// The scheduled refresh against a real database. Each test makes its own small table and changes it behind the API's
/// back, so what a refresh stored and what is in the database now can be told apart: the table holds one row whose
/// number is 1, and "Grow" adds 10. A pass of the scheduler is run by the test, at a time the test chooses.
/// </summary>
[Collection(ApiCollection.Name)]
public class ScheduledRefreshApiTests(ApiFactory factory) : IAsyncLifetime
{
    /// <summary>
    /// The scheduler takes whatever is due anywhere, so each test starts with no dashboard on a schedule: what a pass takes
    /// is then exactly what the test set up. (The tests of this collection run one after another.)
    /// </summary>
    public Task InitializeAsync() =>
        factory.ExecuteAsOwnerAsync("UPDATE dashboards SET refresh_interval_minutes = NULL, next_refresh_utc = NULL WHERE refresh_interval_minutes IS NOT NULL");

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Board(Guid Id, Guid Chart, Guid Dataset, string Table)
    {
        public string RunUrl(Guid org) => $"/api/organizations/{org}/dashboards/{Id}/run";
    }

    private sealed record Org(HttpClient Owner, Guid Id, Guid Source);

    private async Task<Org> NewOrgAsync()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var source = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Probe DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        return new Org(owner, org.Id, (await source.Content.ReadFromJsonAsync<DataSourceDto>(Json))!.Id);
    }

    /// <summary>A dashboard with one chart: the total of a fresh one-row table.</summary>
    private async Task<Board> NewBoardAsync(Org org, string name = "Board", string? tableOverride = null)
    {
        var table = tableOverride ?? "refresh_probe_" + Guid.NewGuid().ToString("N");
        if (tableOverride is null)
            await factory.ExecuteAsOwnerAsync($"CREATE TABLE {table} (label text, n int); INSERT INTO {table} VALUES ('a', 1); GRANT SELECT ON {table} TO reader;");

        var dataset = (await ReadAsync(await org.Owner.PostAsJsonAsync($"/api/organizations/{org.Id}/datasets",
            new { dataSourceId = org.Source, name = name + " data", sql = $"SELECT label, n FROM {table}" }))).GetProperty("id").GetGuid();
        var chart = (await ReadAsync(await org.Owner.PostAsJsonAsync($"/api/organizations/{org.Id}/charts", new
        {
            name = name + " total", datasetId = dataset, type = "Number",
            definition = new { query = new { measures = new[] { new { aggregation = "Sum", column = "n", alias = "total" } } } },
        }))).GetProperty("id").GetGuid();
        var dashboard = (await ReadAsync(await org.Owner.PostAsJsonAsync($"/api/organizations/{org.Id}/dashboards",
            new { name, definition = new { widgets = new object[] { new { chartId = chart } }, filters = Array.Empty<object>() } }))).GetProperty("id").GetGuid();
        return new Board(dashboard, chart, dataset, table);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> SetScheduleRaw(HttpClient client, Org org, Board board, int? minutes) =>
        client.PutAsJsonAsync($"/api/organizations/{org.Id}/dashboards/{board.Id}/refresh", new { intervalMinutes = minutes });

    private static async Task<JsonElement> SetScheduleAsync(Org org, Board board, int? minutes) =>
        (await ReadAsync(await SetScheduleRaw(org.Owner, org, board, minutes))).GetProperty("refresh");

    private static async Task<JsonElement> StatusAsync(Org org, Board board) =>
        (await ReadAsync(await org.Owner.GetAsync($"/api/organizations/{org.Id}/dashboards/{board.Id}"))).GetProperty("refresh");

    private Task Grow(Board board) => factory.ExecuteAsOwnerAsync($"UPDATE {board.Table} SET n = n + 10");

    /// <summary>One pass of the scheduler, as the background service would run it, judging "due" by the given time.</summary>
    private async Task<RefreshSummary> PassAsync(DateTime? at = null, int max = 50)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RefreshDueDashboardsHandler>().HandleAsync(at ?? DateTime.UtcNow.AddSeconds(2), max);
    }

    /// <summary>What a viewer's run shows for the dashboard's one chart: the total, and whether it was a stored result.</summary>
    private static async Task<(long Total, bool Stored)> ViewAsync(HttpClient client, Org org, Board board)
    {
        var run = await ReadAsync(await client.PostAsJsonAsync(board.RunUrl(org.Id), new { filters = new { } }));
        var result = run.GetProperty("widgets")[0].GetProperty("result");
        return (result.GetProperty("rows")[0][0].GetInt64(), result.GetProperty("cachedUtc").ValueKind != JsonValueKind.Null);
    }

    private async Task<HttpClient> AddMemberAsync(Org org, string role)
    {
        var (member, email, _) = await NewUserAsync(factory);
        (await org.Owner.PostAsJsonAsync($"/api/organizations/{org.Id}/members", new { email, role })).EnsureSuccessStatusCode();
        return member;
    }

    // ---- the schedule ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dashboard_is_not_refreshed_on_a_schedule_until_an_editor_says_so()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);

        var status = await StatusAsync(org, board);

        Assert.Equal(JsonValueKind.Null, status.GetProperty("intervalMinutes").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastUtc").ValueKind);
        Assert.Equal(0, status.GetProperty("lastFailures").GetInt32());
    }

    [Fact]
    public async Task An_editor_puts_a_dashboard_on_a_schedule_and_everyone_who_can_see_it_sees_that()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        var viewer = await AddMemberAsync(org, "Viewer");

        var before = DateTime.UtcNow;
        var set = await SetScheduleAsync(org, board, 15);

        Assert.Equal(15, set.GetProperty("intervalMinutes").GetInt32());
        Assert.InRange(set.GetProperty("nextUtc").GetDateTime(), before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1)); // the first refresh is due now
        var seen = (await ReadAsync(await viewer.GetAsync($"/api/organizations/{org.Id}/dashboards/{board.Id}"))).GetProperty("refresh");
        Assert.Equal(15, seen.GetProperty("intervalMinutes").GetInt32());
    }

    [Fact]
    public async Task Editing_the_dashboard_keeps_its_schedule()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        await SetScheduleAsync(org, board, 30);

        var edit = await org.Owner.PutAsJsonAsync($"/api/organizations/{org.Id}/dashboards/{board.Id}", new
        {
            name = "Renamed", definition = new { widgets = new object[] { new { chartId = board.Chart } }, filters = Array.Empty<object>() },
        });

        Assert.Equal(30, (await ReadAsync(edit)).GetProperty("refresh").GetProperty("intervalMinutes").GetInt32());
    }

    [Fact]
    public async Task The_schedule_can_be_taken_off_again()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        await SetScheduleAsync(org, board, 15);

        var off = await SetScheduleAsync(org, board, null);

        Assert.Equal(JsonValueKind.Null, off.GetProperty("intervalMinutes").ValueKind);
        Assert.Equal(JsonValueKind.Null, off.GetProperty("nextUtc").ValueKind);
        Assert.Equal(0, (await PassAsync(DateTime.UtcNow.AddDays(1))).Claimed); // nothing is due: it is off
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(1_441)]
    [InlineData(-10)]
    public async Task An_interval_that_is_too_short_or_too_long_is_refused(int minutes)
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);

        var response = await SetScheduleRaw(org.Owner, org, board, minutes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("5 to 1440", await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, (await StatusAsync(org, board)).GetProperty("intervalMinutes").ValueKind);
    }

    [Fact]
    public async Task Only_editors_set_the_schedule()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        var viewer = await AddMemberAsync(org, "Viewer");
        var (stranger, _, _) = await NewUserAsync(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await SetScheduleRaw(viewer, org, board, 15)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetScheduleRaw(stranger, org, board, 15)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetScheduleRaw(factory.CreateApiClient(), org, board, 15)).StatusCode);
    }

    [Fact]
    public async Task An_organization_has_at_most_twenty_scheduled_dashboards()
    {
        var org = await NewOrgAsync();
        var boards = new List<Board>();
        for (var i = 0; i < 20; i++)
        {
            var board = await NewBoardAsync(org, $"Board {i}");
            await SetScheduleAsync(org, board, 60);
            boards.Add(board);
        }
        var twentyFirst = await NewBoardAsync(org, "One too many");

        var refused = await SetScheduleRaw(org.Owner, org, twentyFirst, 60);
        var changing = await SetScheduleAsync(org, boards[0], 30); // one that is already on a schedule is not "another"
        await SetScheduleAsync(org, boards[1], null);
        var nowFits = await SetScheduleAsync(org, twentyFirst, 60);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("At most 20", await refused.Content.ReadAsStringAsync());
        Assert.Equal(30, changing.GetProperty("intervalMinutes").GetInt32());
        Assert.Equal(60, nowFits.GetProperty("intervalMinutes").GetInt32());
    }

    // ---- a pass of the scheduler -------------------------------------------------------------------------------

    [Fact]
    public async Task A_refresh_fetches_fresh_data_and_stores_it_so_the_next_viewer_is_served_it_without_asking_the_database()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        var viewer = await AddMemberAsync(org, "Viewer");
        await SetScheduleAsync(org, board, 15);
        await Grow(board); // the database now says 11; nothing has stored anything yet

        var pass = await PassAsync();
        await Grow(board); // and now 21, behind the refresh's back

        Assert.Equal(1, pass.Claimed);
        Assert.Equal(1, pass.Refreshed);
        var seen = await ViewAsync(viewer, org, board);
        Assert.Equal((11L, true), seen); // what the refresh fetched, served from storage, though the dataset keeps nothing itself
    }

    [Fact]
    public async Task A_refresh_is_recorded_on_the_dashboard_and_the_next_one_is_an_interval_away()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        await SetScheduleAsync(org, board, 15);
        var at = DateTime.UtcNow.AddSeconds(2);

        await PassAsync(at);

        var status = await StatusAsync(org, board);
        Assert.Equal(at, status.GetProperty("lastUtc").GetDateTime(), TimeSpan.FromMilliseconds(5));
        Assert.Equal(0, status.GetProperty("lastFailures").GetInt32());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastMessage").ValueKind);
        Assert.Equal(at.AddMinutes(15), status.GetProperty("nextUtc").GetDateTime(), TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public async Task A_dashboard_is_refreshed_once_per_interval_not_every_pass()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        await SetScheduleAsync(org, board, 15);
        var start = DateTime.UtcNow.AddSeconds(2);

        var first = await PassAsync(start);
        var tooSoon = await PassAsync(start.AddMinutes(14));
        var due = await PassAsync(start.AddMinutes(16));

        Assert.Equal(1, first.Claimed);
        Assert.Equal(0, tooSoon.Claimed);
        Assert.Equal(1, due.Claimed);
    }

    [Fact]
    public async Task A_dashboard_not_on_a_schedule_is_never_refreshed()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        var viewer = await AddMemberAsync(org, "Viewer");

        var pass = await PassAsync(DateTime.UtcNow.AddDays(30));
        await Grow(board);

        Assert.Equal(0, pass.Claimed);
        Assert.Equal((11L, false), await ViewAsync(viewer, org, board));
    }

    [Fact]
    public async Task A_refresh_is_not_done_for_a_dashboard_whose_schedule_was_just_turned_off()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        await SetScheduleAsync(org, board, 15);
        await SetScheduleAsync(org, board, null);

        var pass = await PassAsync();

        Assert.Equal(0, pass.Claimed);
    }

    [Fact]
    public async Task What_a_refresh_stored_lasts_about_two_intervals_so_a_late_refresh_does_not_send_everyone_to_the_database()
    {
        // Seen from outside: after a refresh the stored answer is served at once, and a manual refresh by an editor keeps it just as long.
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        var viewer = await AddMemberAsync(org, "Viewer");
        await SetScheduleAsync(org, board, 15);
        await PassAsync();
        await Grow(board);

        var manual = await org.Owner.PostAsJsonAsync(board.RunUrl(org.Id), new { filters = new { }, refresh = true });
        await ReadAsync(manual); // an editor asks again: 11 is now what is stored
        await Grow(board);

        Assert.Equal((11L, true), await ViewAsync(viewer, org, board));
    }

    [Fact]
    public async Task One_chart_that_cannot_be_refreshed_is_recorded_and_the_others_are_still_done()
    {
        var org = await NewOrgAsync();
        var broken = await NewBoardAsync(org, "Broken");
        var fine = await NewBoardAsync(org, "Fine");
        await SetScheduleAsync(org, broken, 15);
        await SetScheduleAsync(org, fine, 15);
        await factory.ExecuteAsOwnerAsync($"DROP TABLE {broken.Table}"); // its data is gone

        var pass = await PassAsync();

        Assert.Equal(2, pass.Claimed);
        Assert.Equal(1, pass.Refreshed);
        Assert.Equal(1, pass.Problems);
        var failed = await StatusAsync(org, broken);
        Assert.Equal(1, failed.GetProperty("lastFailures").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(failed.GetProperty("lastMessage").GetString()));
        Assert.Equal(0, (await StatusAsync(org, fine)).GetProperty("lastFailures").GetInt32());
    }

    [Fact]
    public async Task A_failed_refresh_is_tried_again_at_the_next_interval_and_a_later_success_clears_the_problem()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        await SetScheduleAsync(org, board, 15);
        var start = DateTime.UtcNow.AddSeconds(2);
        await factory.ExecuteAsOwnerAsync($"ALTER TABLE {board.Table} RENAME TO {board.Table}_away");
        await PassAsync(start);
        var failing = await StatusAsync(org, board);

        var soon = await PassAsync(start.AddMinutes(1)); // not retried in a hurry
        await factory.ExecuteAsOwnerAsync($"ALTER TABLE {board.Table}_away RENAME TO {board.Table}");
        var later = await PassAsync(start.AddMinutes(16));

        Assert.Equal(1, failing.GetProperty("lastFailures").GetInt32());
        Assert.Equal(0, soon.Claimed);
        Assert.Equal(1, later.Refreshed);
        var cleared = await StatusAsync(org, board);
        Assert.Equal(0, cleared.GetProperty("lastFailures").GetInt32());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("lastMessage").ValueKind);
    }

    [Fact]
    public async Task A_refresh_never_brings_the_sql_to_anyone()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        var viewer = await AddMemberAsync(org, "Viewer");
        await SetScheduleAsync(org, board, 15);
        await PassAsync();

        var run = await ReadAsync(await viewer.PostAsJsonAsync(board.RunUrl(org.Id), new { filters = new { } }));
        var asEditor = await ReadAsync(await org.Owner.PostAsJsonAsync(board.RunUrl(org.Id), new { filters = new { } }));

        Assert.Equal(JsonValueKind.Null, run.GetProperty("widgets")[0].GetProperty("result").GetProperty("sql").ValueKind);
        // An editor still gets the SQL with the stored result: it was kept beside it.
        Assert.Contains("SELECT", asEditor.GetProperty("widgets")[0].GetProperty("result").GetProperty("sql").GetString());
    }

    [Fact]
    public async Task A_visitor_with_a_share_link_is_served_what_a_refresh_stored()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        await SetScheduleAsync(org, board, 15);
        await PassAsync();
        await Grow(board);
        var link = await ReadAsync(await org.Owner.PostAsJsonAsync($"/api/organizations/{org.Id}/dashboards/{board.Id}/shares", new { expiresInDays = 1 }));

        var run = await ReadAsync(await factory.CreateApiClient().PostAsJsonAsync(
            $"/api/shared/dashboards/{link.GetProperty("token").GetString()}/run", new { filters = new { } }));

        var result = run.GetProperty("widgets")[0].GetProperty("result");
        Assert.Equal(1, result.GetProperty("rows")[0][0].GetInt64()); // the stored answer, not the 11 now in the database
        Assert.NotEqual(JsonValueKind.Null, result.GetProperty("cachedUtc").ValueKind);
    }

    [Fact]
    public async Task The_share_page_data_does_not_reveal_the_schedule()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        await SetScheduleAsync(org, board, 15);
        var link = await ReadAsync(await org.Owner.PostAsJsonAsync($"/api/organizations/{org.Id}/dashboards/{board.Id}/shares", new { expiresInDays = 1 }));

        var page = await factory.CreateApiClient().GetStringAsync($"/api/shared/dashboards/{link.GetProperty("token").GetString()}");

        Assert.DoesNotContain("refresh", page, StringComparison.OrdinalIgnoreCase);
    }

    // ---- several servers, and a lot of dashboards --------------------------------------------------------------

    [Fact]
    public async Task Servers_that_pass_at_the_same_moment_never_take_the_same_dashboard()
    {
        var org = await NewOrgAsync();
        var boards = new List<Board>();
        for (var i = 0; i < 8; i++)
        {
            var board = await NewBoardAsync(org, $"Busy {i}");
            await SetScheduleAsync(org, board, 15);
            boards.Add(board);
        }
        var at = DateTime.UtcNow.AddSeconds(2);

        var passes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PassAsync(at)));

        var recorded = 0;
        foreach (var board in boards)
        {
            var status = await StatusAsync(org, board);
            if (status.GetProperty("lastUtc").ValueKind != JsonValueKind.Null) recorded++;
        }
        Assert.Equal(8, recorded); // every one was refreshed ...
        Assert.Equal(8, passes.Sum(p => p.Claimed)); // ... by exactly one of the passes: none was taken twice
        Assert.Equal(0, (await PassAsync(at)).Claimed); // and none is due again at the same moment
    }

    [Fact]
    public async Task One_pass_takes_at_most_its_batch_and_leaves_the_rest_for_the_next()
    {
        var org = await NewOrgAsync();
        for (var i = 0; i < 5; i++) await SetScheduleAsync(org, await NewBoardAsync(org, $"Batch {i}"), 15);
        var at = DateTime.UtcNow.AddSeconds(2);

        var first = await PassAsync(at, max: 2);

        Assert.Equal(2, first.Claimed);
        Assert.Equal(2, (await PassAsync(at, max: 2)).Claimed);
        Assert.Equal(1, (await PassAsync(at, max: 2)).Claimed);
        Assert.Equal(0, (await PassAsync(at, max: 2)).Claimed);
    }

    // ---- the background service --------------------------------------------------------------------------------

    private DashboardRefreshService Service(bool enabled, int pollSeconds = 1) =>
        new(factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Services.GetRequiredService<IClock>(),
            Options.Create(new DashboardRefreshOptions { Enabled = enabled, PollSeconds = pollSeconds, BatchSize = 50 }),
            NullLogger<DashboardRefreshService>.Instance);

    private static async Task<bool> WaitAsync(Func<Task<bool>> condition, int seconds = 15)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            if (await condition()) return true;
            await Task.Delay(200);
        }
        return false;
    }

    [Fact]
    public async Task The_background_service_refreshes_a_dashboard_that_is_due_without_anyone_asking()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        await SetScheduleAsync(org, board, 15);
        var service = Service(enabled: true);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var refreshed = await WaitAsync(async () => (await StatusAsync(org, board)).GetProperty("lastUtc").ValueKind != JsonValueKind.Null);
            Assert.True(refreshed, "the service should have refreshed the dashboard within a few passes");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task The_background_service_does_nothing_when_it_is_turned_off()
    {
        var org = await NewOrgAsync();
        var board = await NewBoardAsync(org);
        await SetScheduleAsync(org, board, 15);
        var service = Service(enabled: false);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(2_500);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(JsonValueKind.Null, (await StatusAsync(org, board)).GetProperty("lastUtc").ValueKind);
    }

    [Fact]
    public async Task The_background_service_stops_promptly_when_asked()
    {
        var service = Service(enabled: true, pollSeconds: 30);

        await service.StartAsync(CancellationToken.None);
        var stopped = service.StopAsync(CancellationToken.None);

        Assert.True(await Task.WhenAny(stopped, Task.Delay(5_000)) == stopped, "it should not sit out its wait");
    }
}
