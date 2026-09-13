using System.Text.Json;
using DataBoard.Application.Charts;
using DataBoard.Application.DataSources;
using DataBoard.Application.Organizations;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Tests;

/// <summary>How chart runs use the result cache: when it is read, when it is written, for how long, and what tells two answers apart.</summary>
public class ResultCacheTests
{
    private readonly FakeDatasetRepo _datasets = new();
    private readonly FakeDataSourceRepo _sources = new();
    private readonly FakeOrganizationRepo _orgs = new();
    private readonly FakeCurrentUser _user = new(Guid.NewGuid());
    private readonly FakeConnector _connector = new();
    private readonly FakeResultCache _cache = new();
    private readonly MutableClock _clock = new();
    private readonly Guid _org = Guid.NewGuid();
    private readonly DataSource _source;
    private Dataset _dataset;

    private static readonly ChartQuery Count = new(Measures: [new(Aggregation.Count)]);

    public ResultCacheTests()
    {
        _source = DataSource.Create(_org, "DB", DataSourceType.PostgreSql, new FakeSecrets().Protect("Host=db;Password=secret"), DateTime.UtcNow);
        _sources.Items.Add(_source);
        _dataset = NewDataset("SELECT region, amount FROM sales");
        _datasets.Items.Add(_dataset);
        _orgs.Members.Add(OrganizationMember.Create(_org, _user.UserId!.Value, OrganizationRole.Editor, DateTime.UtcNow));
    }

    private Dataset NewDataset(string sql, int cacheSeconds = 0)
    {
        var dataset = Dataset.Create(_org, _source.Id, "Orders" + Guid.NewGuid().ToString("N")[..4], null, sql,
            [new("region", "text", ColumnRole.Dimension), new("amount", "numeric", ColumnRole.Measure)], Guid.NewGuid(), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        dataset.SetCacheSeconds(cacheSeconds);
        return dataset;
    }

    private RunChartQueryHandler Handler() => new(
        _datasets, _sources, new FakeSecrets(), new DataSourceConnectors([_connector]),
        new ChartSqlRenderers([new CountingRenderer()]), new FakeValidator(), _cache, _clock, new OrganizationAccess(_orgs, _user));

    /// <summary>Writes the values it was given into the SQL, so two different bound values give two different queries.</summary>
    private sealed class CountingRenderer : IChartSqlRenderer
    {
        public DataSourceType Type => DataSourceType.PostgreSql;

        public ParameterizedQuery Render(string datasetSql, ChartQueryPlan plan, IReadOnlyList<object?>? datasetParameters = null) =>
            new($"RENDERED({datasetSql}) filters={plan.Filters.Count}:{string.Join(',', plan.Filters.SelectMany(f => f.Values))}", datasetParameters ?? []);
    }

    private static CacheRequest Refresh(TimeSpan? storeFor = null) => new(CacheMode.Refresh, storeFor);

    // ---- when it is used ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dataset_that_keeps_nothing_is_asked_every_time_and_nothing_is_stored()
    {
        var handler = Handler();

        await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);
        _connector.Answer = 4;
        var second = await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);

        Assert.Equal(4L, second.Rows.Single()[0]);
        Assert.Equal(2, _connector.ExecuteCalls);
        Assert.Equal(0, _cache.Sets);
        Assert.Null(second.CachedUtc);
    }

    [Fact]
    public async Task A_dataset_that_keeps_results_gives_the_stored_answer_until_it_expires()
    {
        _datasets.Items[0] = _dataset = NewDataset("SELECT region, amount FROM sales", cacheSeconds: 300);
        var handler = Handler();

        var first = await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);
        _connector.Answer = 99; // the data changed in the database
        var second = await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);

        Assert.Equal(3L, first.Rows.Single()[0]);
        Assert.Null(first.CachedUtc); // fetched just now
        Assert.Equal(3L, second.Rows.Single()[0]); // still the stored one
        Assert.Equal(_clock.UtcNow, second.CachedUtc);
        Assert.Equal(1, _connector.ExecuteCalls);
        Assert.Equal(TimeSpan.FromSeconds(300), _cache.Items.Single().Value.Ttl);
    }

    [Fact]
    public async Task A_live_run_neither_reads_nor_writes_the_cache()
    {
        _datasets.Items[0] = _dataset = NewDataset("SELECT region, amount FROM sales", cacheSeconds: 300);
        var handler = Handler();
        await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use); // something is stored
        var sets = _cache.Sets;
        var gets = _cache.Gets;
        _connector.Answer = 7;

        var live = await handler.HandleAsync(_org, _dataset.Id, Count); // the chart editor's preview

        Assert.Equal(7L, live.Rows.Single()[0]);
        Assert.Equal((sets, gets), (_cache.Sets, _cache.Gets));
    }

    [Fact]
    public async Task A_refresh_asks_the_database_and_stores_the_new_answer_for_the_next_run()
    {
        _datasets.Items[0] = _dataset = NewDataset("SELECT region, amount FROM sales", cacheSeconds: 300);
        var handler = Handler();
        await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);
        _connector.Answer = 8;

        var refreshed = await handler.HandleAsync(_org, _dataset.Id, Count, Refresh());
        _connector.Answer = 9;
        var after = await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);

        Assert.Equal(8L, refreshed.Rows.Single()[0]);
        Assert.Null(refreshed.CachedUtc);
        Assert.Equal(8L, after.Rows.Single()[0]); // the refreshed answer, not 9
        Assert.Equal(2, _connector.ExecuteCalls);
    }

    [Fact]
    public async Task A_refresh_stores_nothing_for_a_dataset_that_keeps_nothing_unless_asked_to()
    {
        var handler = Handler();

        await handler.HandleAsync(_org, _dataset.Id, Count, Refresh());
        Assert.Equal(0, _cache.Sets);

        await handler.HandleAsync(_org, _dataset.Id, Count, Refresh(TimeSpan.FromMinutes(20))); // a schedule wants it kept
        Assert.Equal(TimeSpan.FromMinutes(20), _cache.Items.Single().Value.Ttl);

        _connector.Answer = 1;
        var fromCache = await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use); // so viewers are served it
        Assert.Equal(3L, fromCache.Rows.Single()[0]);
    }

    [Fact]
    public async Task A_refresh_keeps_the_result_as_long_as_the_longer_of_the_dataset_and_the_schedule()
    {
        _datasets.Items[0] = _dataset = NewDataset("SELECT region, amount FROM sales", cacheSeconds: 3600);
        var handler = Handler();

        await handler.HandleAsync(_org, _dataset.Id, Count, Refresh(TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromHours(1), _cache.Items.Single().Value.Ttl);

        _cache.Items.Clear();
        await handler.HandleAsync(_org, _dataset.Id, Count, Refresh(TimeSpan.FromHours(2)));
        Assert.Equal(TimeSpan.FromHours(2), _cache.Items.Single().Value.Ttl);
    }

    // ---- who sees what -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_stored_result_never_hands_the_sql_to_someone_who_may_not_see_it()
    {
        _datasets.Items[0] = _dataset = NewDataset("SELECT region, amount FROM sales", cacheSeconds: 300);
        var handler = Handler();
        var asEditor = await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use); // an editor stores it
        Assert.NotNull(asEditor.Sql);

        var viewer = new FakeCurrentUser(Guid.NewGuid());
        _orgs.Members.Add(OrganizationMember.Create(_org, viewer.UserId!.Value, OrganizationRole.Viewer, DateTime.UtcNow));
        var viewerHandler = new RunChartQueryHandler(
            _datasets, _sources, new FakeSecrets(), new DataSourceConnectors([_connector]),
            new ChartSqlRenderers([new CountingRenderer()]), new FakeValidator(), _cache, _clock, new OrganizationAccess(_orgs, viewer));

        var asViewer = await viewerHandler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);

        Assert.NotNull(asViewer.CachedUtc); // it was served from the cache ...
        Assert.Null(asViewer.Sql); // ... without the SQL that was stored with it
        Assert.Equal(1, _connector.ExecuteCalls);
    }

    [Fact]
    public async Task A_trusted_run_uses_the_cache_too_and_never_returns_sql()
    {
        _datasets.Items[0] = _dataset = NewDataset("SELECT region, amount FROM sales", cacheSeconds: 300);
        var handler = Handler();
        await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);

        var trusted = await handler.HandleTrustedAsync(_org, _dataset.Id, Count, CacheRequest.Use);

        Assert.NotNull(trusted.CachedUtc);
        Assert.Null(trusted.Sql);
        Assert.Equal(1, _connector.ExecuteCalls);
    }

    // ---- what tells two answers apart --------------------------------------------------------------------------

    [Fact]
    public async Task Different_filters_are_different_answers()
    {
        _datasets.Items[0] = _dataset = NewDataset("SELECT region, amount FROM sales", cacheSeconds: 300);
        var handler = Handler();
        ChartQuery In(string region) => Count with { Filters = [new("region", FilterOperator.Eq, JsonSerializer.SerializeToElement(region))] };

        await handler.HandleAsync(_org, _dataset.Id, In("Auckland"), CacheRequest.Use);
        _connector.Answer = 5;
        var other = await handler.HandleAsync(_org, _dataset.Id, In("Dunedin"), CacheRequest.Use);
        var again = await handler.HandleAsync(_org, _dataset.Id, In("Auckland"), CacheRequest.Use);

        Assert.Equal(5L, other.Rows.Single()[0]); // not Auckland's answer
        Assert.Equal(3L, again.Rows.Single()[0]); // Auckland's, still stored
        Assert.Equal(2, _connector.ExecuteCalls);
    }

    [Fact]
    public async Task Different_parameter_values_are_different_answers()
    {
        var withParameter = NewDataset("SELECT region, amount FROM sales WHERE region = {{region}}", cacheSeconds: 300);
        typeof(Dataset).GetProperty(nameof(Dataset.ParametersJson))!.SetValue(
            withParameter, """[{"name":"region","type":"Text","default":"Auckland"}]""");
        _datasets.Items.Add(withParameter);
        var handler = Handler();
        ChartQuery For(string? region) => Count with
        {
            Parameters = region is null ? null : new Dictionary<string, JsonElement> { ["region"] = JsonSerializer.SerializeToElement(region) },
        };

        await handler.HandleAsync(_org, withParameter.Id, For(null), CacheRequest.Use); // the default, Auckland
        _connector.Answer = 6;
        var wellington = await handler.HandleAsync(_org, withParameter.Id, For("Wellington"), CacheRequest.Use);
        var explicitDefault = await handler.HandleAsync(_org, withParameter.Id, For("Auckland"), CacheRequest.Use);

        Assert.Equal(6L, wellington.Rows.Single()[0]);
        Assert.Equal(3L, explicitDefault.Rows.Single()[0]); // asking for the default by name is the same question
        Assert.Equal(2, _connector.ExecuteCalls);
    }

    [Fact]
    public async Task Changing_the_dataset_gives_a_fresh_answer_because_the_old_one_is_no_longer_asked_for()
    {
        _datasets.Items[0] = _dataset = NewDataset("SELECT region, amount FROM sales", cacheSeconds: 300);
        var handler = Handler();
        await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);
        _connector.Answer = 12;

        _dataset.Update(_dataset.Name, null, "SELECT region, amount FROM sales WHERE amount > 0", _dataset.Columns, _clock.UtcNow.AddMinutes(1));
        var after = await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);

        Assert.Equal(12L, after.Rows.Single()[0]);
        Assert.Equal(2, _connector.ExecuteCalls);
    }

    [Fact]
    public async Task Changing_how_long_results_are_kept_starts_afresh_so_turning_it_off_shows_live_data_at_once()
    {
        _datasets.Items[0] = _dataset = NewDataset("SELECT region, amount FROM sales", cacheSeconds: 300);
        var handler = Handler();
        await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);
        _connector.Answer = 12;

        _dataset.SetCacheSeconds(0);
        var off = await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);
        _connector.Answer = 13;
        var stillOff = await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);

        Assert.Equal(12L, off.Rows.Single()[0]); // not the stored 3
        Assert.Equal(13L, stillOff.Rows.Single()[0]); // and nothing is stored now
        Assert.Equal(3, _connector.ExecuteCalls);
    }
    [Fact]
    public async Task Two_datasets_with_the_same_sql_do_not_share_answers()
    {
        var other = NewDataset("SELECT region, amount FROM sales", cacheSeconds: 300);
        _datasets.Items[0] = _dataset = NewDataset("SELECT region, amount FROM sales", cacheSeconds: 300);
        _datasets.Items.Add(other);
        var handler = Handler();

        await handler.HandleAsync(_org, _dataset.Id, Count, CacheRequest.Use);
        _connector.Answer = 2;
        var second = await handler.HandleAsync(_org, other.Id, Count, CacheRequest.Use);

        Assert.Equal(2L, second.Rows.Single()[0]);
        Assert.Equal(2, _connector.ExecuteCalls);
    }

    [Fact]
    public void The_key_covers_everything_that_can_change_the_answer()
    {
        var org = Guid.NewGuid();
        var dataset = Guid.NewGuid();
        var source = Guid.NewGuid();
        var updated = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var query = new ParameterizedQuery("SELECT 1", ["a", 2m]);
        string Key(Guid? o = null, Guid? d = null, DateTime? u = null, int cache = 300, Guid? s = null, string c = "enc", ParameterizedQuery? q = null, int limit = 100) =>
            ResultCacheKey.For(o ?? org, d ?? dataset, u ?? updated, cache, s ?? source, c, q ?? query, limit);

        var keys = new[]
        {
            Key(), Key(o: Guid.NewGuid()), Key(d: Guid.NewGuid()), Key(u: updated.AddTicks(1)), Key(cache: 0), Key(cache: 600), Key(s: Guid.NewGuid()), Key(c: "other"),
            Key(q: new ParameterizedQuery("SELECT 2", ["a", 2m])), Key(q: new ParameterizedQuery("SELECT 1", ["b", 2m])),
            Key(q: new ParameterizedQuery("SELECT 1", ["a", 3m])), Key(q: new ParameterizedQuery("SELECT 1", ["a", "2"])), Key(limit: 101),
        };

        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal(Key(), Key()); // and the same question always gets the same key
        Assert.DoesNotContain("enc", Key()); // the connection is hashed, never stored in a key
    }

    [Fact]
    public void Values_cannot_be_run_together_to_make_two_different_questions_look_alike()
    {
        var one = ResultCacheKey.For(Guid.Empty, Guid.Empty, default, 0, Guid.Empty, "c", new ParameterizedQuery("S", ["ab", "c"]), 1);
        var other = ResultCacheKey.For(Guid.Empty, Guid.Empty, default, 0, Guid.Empty, "c", new ParameterizedQuery("S", ["a", "bc"]), 1);

        Assert.NotEqual(one, other);
    }
}
