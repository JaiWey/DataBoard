using System.Text;
using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Infrastructure.DataSources;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DataBoard.Infrastructure.Tests;

/// <summary>
/// Reading an imported CSV: that it can only be read, that a query cannot run for ever, what the schema and column types
/// are called, and that the SQL the chart renderer writes for SQLite gives the same answers PostgreSQL's would.
/// </summary>
public sealed class SqliteConnectorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "databoard-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LocalUploadStore _store;
    private readonly SqliteConnector _connector;
    private readonly string _connection;

    private const string Sales = """
        region,amount,qty,sold_on,paid
        North,10.5,1,2026-01-05,true
        North,20,2,2026-01-19,false
        South,5,3,2026-02-02,true
        South,15,4,2026-04-13,true
        ,7,5,2026-04-14,false
        """;

    public SqliteConnectorTests()
    {
        _store = new LocalUploadStore(Options.Create(new UploadOptions { Path = _folder }));
        _connector = new SqliteConnector(_store);
        var key = _store.NewKey();
        new CsvImporter().ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(Sales)), "sales.csv", new CsvImportOptions(), _store.PathOf(key)).GetAwaiter().GetResult();
        _connection = CsvConnection.For(key);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private Task<QueryPreview> Preview(string sql, int max = 100, params object?[] parameters) =>
        _connector.PreviewAsync(_connection, sql, max, parameters);

    // ---- the connection ---------------------------------------------------------------------------------------

    [Fact]
    public void A_connection_string_cannot_be_given_for_an_upload() =>
        Assert.Contains("ConnectionString", Assert.Throws<ValidationException>(() => _connector.ValidateConnectionString("Data Source=/etc/passwd")).Errors.Keys);

    [Fact]
    public async Task Tests_a_file_that_is_there_and_one_that_is_not()
    {
        Assert.True((await _connector.TestAsync(_connection)).Success);

        var gone = await _connector.TestAsync(CsvConnection.For(_store.NewKey()));
        Assert.False(gone.Success);
        Assert.Contains("missing", gone.Message);
    }

    [Theory]
    [InlineData("Data Source=/etc/passwd")]
    [InlineData("Data Source=../../secret.db")]
    [InlineData("Data Source=..\\..\\secret.db")]
    [InlineData("Data Source=")]
    [InlineData("Server=elsewhere")]
    public async Task Never_opens_a_file_that_the_store_did_not_make(string connection)
    {
        var result = await _connector.TestAsync(connection);

        Assert.False(result.Success);
        Assert.Contains("no file", result.Message);
    }

    // ---- schema and types -----------------------------------------------------------------------------------


    [Fact]
    public async Task Lists_the_table_and_its_columns_with_the_names_for_types_the_application_uses()
    {
        var schema = await _connector.GetSchemaAsync(_connection);

        var node = Assert.Single(schema.Schemas);
        Assert.Equal("main", node.Name);
        var table = Assert.Single(node.Tables);
        Assert.Equal("sales", table.Name);
        Assert.Equal(
            ["region:text", "amount:double precision", "qty:bigint", "sold_on:date", "paid:boolean"],
            table.Columns.Select(c => $"{c.Name}:{c.DataType}"));
    }

    [Fact]
    public async Task Describes_the_columns_of_a_query_by_what_they_are_or_what_they_hold()
    {
        var columns = await _connector.DescribeAsync(
            _connection, "SELECT region, SUM(qty) AS total, AVG(amount) AS mean, sold_on, 1 AS one, 'x' AS word, NULL AS blank FROM sales GROUP BY region, sold_on", []);

        Assert.Equal(
            ["region:text", "total:bigint", "mean:double precision", "sold_on:date", "one:bigint", "word:text", "blank:text"],
            columns.Select(c => $"{c.Name}:{c.DataType}"));
    }

    [Fact]
    public async Task Gives_a_true_false_for_a_boolean_column_not_a_one_or_zero()
    {
        var result = await Preview("SELECT paid FROM sales ORDER BY qty");

        Assert.Equal("boolean", result.Columns[0].DataType);
        Assert.Equal(new object[] { true, false, true, true, false }, result.Rows.Select(r => r[0]!));
    }

    [Fact]
    public async Task Cuts_the_rows_at_the_limit_and_says_so()
    {
        var result = await Preview("SELECT qty FROM sales ORDER BY qty", max: 2);

        Assert.True(result.Truncated);
        Assert.Equal(2, result.Rows.Count);
        Assert.False((await Preview("SELECT qty FROM sales", max: 5)).Truncated);
    }

    [Fact]
    public async Task Binds_values_by_name_and_compares_a_number_as_a_number()
    {
        var result = await Preview("SELECT qty FROM sales WHERE region = @databoard_p0 AND amount > @databoard_p1 ORDER BY qty", 100, "North", 10m);

        Assert.Equal([1L, 2L], result.Rows.Select(r => r[0]!));
    }

    [Fact]
    public async Task A_line_comment_at_the_end_of_the_sql_does_not_break_the_query()
    {
        var result = await Preview("SELECT qty FROM sales -- everything");

        Assert.Equal(5, result.Rows.Count);
    }

    // ---- only reading ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("DELETE FROM sales")]
    [InlineData("INSERT INTO sales (region) VALUES ('x')")]
    [InlineData("UPDATE sales SET qty = 0")]
    [InlineData("DROP TABLE sales")]
    [InlineData("CREATE TABLE other (a)")]
    [InlineData("ATTACH DATABASE ':memory:' AS other")]
    [InlineData("PRAGMA query_only = OFF")]
    public async Task Cannot_change_the_data_even_if_such_sql_got_through_the_validator(string sql)
    {
        // The validator refuses all of these. This is the second lock: the file is opened for reading only.
        await Assert.ThrowsAsync<ValidationException>(() => _connector.ExecuteAsync(_connection, new ParameterizedQuery(sql, []), 10));

        Assert.Equal(5L, (await Preview("SELECT count(*) FROM sales")).Rows[0][0]);
    }

    [Fact]
    public async Task Leaves_the_file_exactly_as_it_was()
    {
        var path = _store.PathOf(CsvConnection.TryGetKey(_connection, out var key) ? key : "");
        var before = await File.ReadAllBytesAsync(path);

        await Preview("SELECT * FROM sales");
        await _connector.GetSchemaAsync(_connection);

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + "-journal"));
    }

    [Fact]
    public async Task Stops_a_query_that_would_run_for_ever_and_says_it_took_too_long()
    {
        // A cross join of a counting table with itself, summed: never finishes in any useful time.
        const string sql = """
            WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 100000000)
            SELECT count(*) FROM c a, c b
            """;
        var started = DateTime.UtcNow;

        var error = await Assert.ThrowsAsync<ValidationException>(() => Preview(sql));

        Assert.Contains("took too long", error.Errors["Sql"][0]);
        Assert.InRange((DateTime.UtcNow - started).TotalSeconds, 5, 25);
    }

    [Fact]
    public async Task Refuses_a_value_larger_than_the_limit()
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() => Preview("SELECT length(zeroblob(5000000))"));

        Assert.Contains("too big", error.Errors["Sql"][0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_pattern_that_is_too_long_for_like()
    {
        var pattern = new string('a', 3_000);

        await Assert.ThrowsAsync<ValidationException>(() => Preview("SELECT 'x' LIKE @databoard_p0", 10, pattern));
    }

    [Fact]
    public async Task Tells_the_author_what_is_wrong_in_plain_words_without_the_error_code()
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() => Preview("SELECT nope FROM sales"));

        Assert.Equal("no such column: nope", error.Errors["Sql"][0]);
    }

    [Fact]
    public async Task Cuts_a_long_text_in_the_sample()
    {
        var result = await Preview("SELECT printf('%.*c', 5000, 'x')");

        Assert.Equal(1_001, ((string)result.Rows[0][0]!).Length);
    }

    [Fact]
    public async Task Two_queries_at_once_do_not_disturb_each_other()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Preview($"SELECT {i} + count(*) FROM sales")));

        Assert.Equal(Enumerable.Range(0, 8).Select(i => (long)(i + 5)), results.Select(r => (long)r.Rows[0][0]!));
    }

    // ---- what the chart renderer writes, run for real ---------------------------------------------------------

    private readonly SqliteChartSqlRenderer _renderer = new();
    private const string All = "SELECT * FROM sales";

    private static ChartQueryPlan Plan(
        PlannedDimension[]? dims = null, PlannedMeasure[]? measures = null, PlannedFilter[]? filters = null,
        PlannedOrder[]? order = null, int limit = 100, int? seriesLimit = null) =>
        new(dims ?? [], measures ?? [], filters ?? [], [], order ?? [], limit, seriesLimit);

    private async Task<List<string>> Run(ChartQueryPlan plan)
    {
        var query = _renderer.Render(All, plan);
        var result = await _connector.ExecuteAsync(_connection, query, 100);
        return result.Rows.Select(r => string.Join("|", r.Select(v => v is double d ? d.ToString("0.##") : v?.ToString() ?? "∅"))).ToList();
    }

    [Fact]
    public async Task Sums_by_a_dimension_and_orders_by_the_total()
    {
        var rows = await Run(Plan(
            dims: [new("region", "text", null, "region")],
            measures: [new(Aggregation.Sum, "amount", "total")],
            order: [new(2, SortDirection.Desc)]));

        Assert.Equal(["North|30.5", "South|20", "∅|7"], rows);
    }

    [Fact]
    public async Task Groups_by_the_start_of_a_month_a_quarter_and_a_year()
    {
        string Grain(TimeGrain g) => string.Join(",", Run(Plan(
            dims: [new("sold_on", "date", g, "d")],
            measures: [new(Aggregation.Count, null, "n")],
            order: [new(1, SortDirection.Asc)])).GetAwaiter().GetResult());

        Assert.Equal("2026-01-01|2,2026-02-01|1,2026-04-01|2", Grain(TimeGrain.Month));
        Assert.Equal("2026-01-01|3,2026-04-01|2", Grain(TimeGrain.Quarter));
        Assert.Equal("2026-01-01|5", Grain(TimeGrain.Year));
        Assert.Equal("2026-01-05|1,2026-01-19|1,2026-02-02|1,2026-04-13|1,2026-04-14|1", Grain(TimeGrain.Day));
    }

    [Fact]
    public async Task Starts_a_week_on_the_monday_as_postgresql_does()
    {
        // 2026-01-05, 01-19 and 02-02 are Mondays; 2026-04-14 (a Tuesday) belongs to the week of 2026-04-13.
        var rows = await Run(Plan(
            dims: [new("sold_on", "date", TimeGrain.Week, "w")],
            measures: [new(Aggregation.Count, null, "n")],
            order: [new(1, SortDirection.Asc)]));

        Assert.Equal(["2026-01-05|1", "2026-01-19|1", "2026-02-02|1", "2026-04-13|2"], rows);
    }

    [Fact]
    public async Task Filters_with_a_search_that_ignores_case_and_treats_percent_and_underscore_as_letters()
    {
        var found = await Run(Plan(
            dims: [new("region", "text", null, "region")],
            filters: [new("region", FilterOperator.Contains, ["ORT"])]));
        Assert.Equal(["North"], found);

        var none = await Run(Plan(
            dims: [new("region", "text", null, "region")],
            filters: [new("region", FilterOperator.Contains, ["%"])]));
        Assert.Empty(none);
    }

    [Fact]
    public async Task Finds_missing_values_and_values_that_are_there()
    {
        var count = new[] { new PlannedMeasure(Aggregation.Count, null, "n") };

        Assert.Equal(["1"], await Run(Plan(measures: count, filters: [new("region", FilterOperator.IsNull, [])])));
        Assert.Equal(["4"], await Run(Plan(measures: count, filters: [new("region", FilterOperator.IsNotNull, [])])));
    }

    [Fact]
    public async Task Keeps_only_the_top_series_and_lists_a_missing_series_value_with_the_rest()
    {
        var rows = await Run(Plan(
            dims: [new("sold_on", "date", TimeGrain.Year, "year"), new("region", "text", null, "region")],
            measures: [new(Aggregation.Sum, "amount", "total")],
            order: [new(2, SortDirection.Asc)],
            seriesLimit: 1));

        Assert.Equal(["2026-01-01|North|30.5"], rows);
    }

    [Fact]
    public async Task Counts_distinct_values_and_averages()
    {
        var rows = await Run(Plan(measures:
        [
            new(Aggregation.CountDistinct, "region", "regions"),
            new(Aggregation.Avg, "qty", "mean"),
        ]));

        Assert.Equal(["2|3"], rows);
    }
}
