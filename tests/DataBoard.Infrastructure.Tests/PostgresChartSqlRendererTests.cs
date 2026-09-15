using System.Text.RegularExpressions;
using DataBoard.Application.Charts;
using DataBoard.Infrastructure.DataSources;

namespace DataBoard.Infrastructure.Tests;

public class PostgresChartSqlRendererTests
{
    private readonly PostgresChartSqlRenderer _renderer = new();
    private const string Dataset = "SELECT * FROM sales";

    private static ChartQueryPlan Plan(
        PlannedDimension[]? dims = null, PlannedMeasure[]? measures = null, PlannedFilter[]? filters = null,
        PlannedMeasureFilter[]? having = null, PlannedOrder[]? order = null, int limit = 100, int? seriesLimit = null) =>
        new(dims ?? [], measures ?? [], filters ?? [], having ?? [], order ?? [], limit, seriesLimit);

    private static PlannedDimension Dim(string column, string type = "text", TimeGrain? grain = null, string? alias = null) =>
        new(column, type, grain, alias ?? column);

    private static PlannedMeasure Measure(Aggregation a, string? column, string alias) => new(a, column, alias);

    // ---- overall shape ---------------------------------------------------------------------------------------

    [Fact]
    public void Renders_a_full_grouped_query_with_every_clause_in_order()
    {
        var revenue = Measure(Aggregation.Sum, "price", "revenue");
        var plan = Plan(
            dims: [Dim("region")],
            measures: [revenue],
            filters:
            [
                new("region", FilterOperator.In, ["Auckland", "Wellington"]),
                new("quantity", FilterOperator.Gte, [3m]),
            ],
            having: [new(revenue, FilterOperator.Gt, [1000m])],
            order: [new(2, SortDirection.Desc)],
            limit: 10);

        var query = _renderer.Render(Dataset, plan);

        Assert.Equal(
            "SELECT \"region\" AS \"region\", SUM(\"price\") AS \"revenue\"\n" +
            "FROM (\nSELECT * FROM sales\n) AS \"databoard_dataset\"\n" +
            "WHERE \"region\" IN (@databoard_p0, @databoard_p1) AND \"quantity\" >= @databoard_p2\n" +
            "GROUP BY 1\n" +
            "HAVING SUM(\"price\") > @databoard_p3\n" +
            "ORDER BY 2 DESC\n" +
            "LIMIT 11",
            query.Sql);
        Assert.Equal(new object[] { "Auckland", "Wellington", 3m, 1000m }, query.Parameters);
    }

    [Fact]
    public void A_series_limit_keeps_only_the_top_series_by_the_first_measure()
    {
        var plan = Plan(
            dims: [Dim("at", "date", TimeGrain.Month, "month"), Dim("region")],
            measures: [Measure(Aggregation.Sum, "price", "revenue")],
            filters: [new("quantity", FilterOperator.Gte, [3m])],
            order: [new(1, SortDirection.Asc)],
            limit: 10,
            seriesLimit: 5);

        var query = _renderer.Render(Dataset, plan);

        Assert.Equal(
            "WITH \"databoard_base\" AS (\n" +
            "SELECT date_trunc('month', \"at\"::timestamp) AS \"month\", \"region\" AS \"region\", SUM(\"price\") AS \"revenue\"\n" +
            "FROM (\nSELECT * FROM sales\n) AS \"databoard_dataset\"\n" +
            "WHERE \"quantity\" >= @databoard_p0\n" +
            "GROUP BY 1, 2\n" +
            "), \"databoard_top_series\" AS (\n" +
            "SELECT \"region\" FROM \"databoard_base\" GROUP BY 1 ORDER BY SUM(\"revenue\") DESC NULLS LAST, 1 LIMIT 5\n" +
            ")\n" +
            "SELECT b.* FROM \"databoard_base\" AS b WHERE EXISTS (SELECT 1 FROM \"databoard_top_series\" AS t WHERE t.\"region\" IS NOT DISTINCT FROM b.\"region\")\n" +
            "ORDER BY 1 ASC\n" +
            "LIMIT 11",
            query.Sql);
        Assert.Equal(new object[] { 3m }, query.Parameters);
    }

    [Fact]
    public void A_datasets_own_parameters_come_first_and_filter_values_continue_the_numbering()
    {
        var plan = Plan(
            measures: [Measure(Aggregation.Count, null, "n")],
            filters: [new("region", FilterOperator.Eq, ["Auckland"]), new("price", FilterOperator.Between, [10m, 20m])]);

        var query = _renderer.Render("SELECT * FROM sales WHERE a = @databoard_p0 AND b = @databoard_p1", plan, ["first", 7m]);

        Assert.Contains("FROM (\nSELECT * FROM sales WHERE a = @databoard_p0 AND b = @databoard_p1\n)", query.Sql);
        Assert.Contains("\"region\" = @databoard_p2 AND \"price\" BETWEEN @databoard_p3 AND @databoard_p4", query.Sql);
        Assert.Equal(new object[] { "first", 7m, "Auckland", 10m, 20m }, query.Parameters);
    }

    [Fact]
    public void Dataset_parameters_are_kept_when_the_query_is_cut_down_to_its_top_series()
    {
        var plan = Plan(
            dims: [Dim("at", "date", TimeGrain.Month, "month"), Dim("region")],
            measures: [Measure(Aggregation.Sum, "price", "revenue")],
            filters: [new("quantity", FilterOperator.Gte, [3m])],
            seriesLimit: 3);

        var query = _renderer.Render("SELECT * FROM sales WHERE a = @databoard_p0", plan, ["x"]);

        Assert.StartsWith("WITH \"databoard_base\" AS (", query.Sql);
        Assert.Contains("\"quantity\" >= @databoard_p1", query.Sql);
        Assert.Equal(new object[] { "x", 3m }, query.Parameters);
    }

    [Fact]
    public void Without_dataset_parameters_the_numbering_starts_at_zero() =>
        Assert.Equal(new object[] { "Auckland" }, _renderer.Render(Dataset,
            Plan(measures: [Measure(Aggregation.Count, null, "n")], filters: [new("region", FilterOperator.Eq, ["Auckland"])])).Parameters);

    [Fact]
    public void Without_a_series_limit_there_is_no_cte() =>
        Assert.DoesNotContain("WITH", _renderer.Render(Dataset,
            Plan(dims: [Dim("a"), Dim("b")], measures: [Measure(Aggregation.Count, null, "n")])).Sql);

    [Fact]
    public void Only_measures_means_no_group_by_and_no_where_or_having()
    {
        var query = _renderer.Render(Dataset, Plan(measures: [Measure(Aggregation.Count, null, "n")]));

        Assert.Equal(
            "SELECT COUNT(*) AS \"n\"\nFROM (\nSELECT * FROM sales\n) AS \"databoard_dataset\"\nLIMIT 101",
            query.Sql);
        Assert.Empty(query.Parameters);
    }

    [Fact]
    public void Group_by_and_order_by_use_positions_so_expressions_are_not_repeated()
    {
        var plan = Plan(
            dims: [Dim("a"), Dim("b"), Dim("c")],
            measures: [Measure(Aggregation.Count, null, "n")],
            order: [new(4, SortDirection.Desc), new(1, SortDirection.Asc)]);

        var sql = _renderer.Render(Dataset, plan).Sql;

        Assert.Contains("\nGROUP BY 1, 2, 3\n", sql);
        Assert.Contains("\nORDER BY 4 DESC, 1 ASC\n", sql);
    }

    [Theory]
    [InlineData(Aggregation.Sum, "SUM(\"x\")")]
    [InlineData(Aggregation.Avg, "AVG(\"x\")")]
    [InlineData(Aggregation.Min, "MIN(\"x\")")]
    [InlineData(Aggregation.Max, "MAX(\"x\")")]
    [InlineData(Aggregation.Count, "COUNT(\"x\")")]
    [InlineData(Aggregation.CountDistinct, "COUNT(DISTINCT \"x\")")]
    public void Each_aggregation_has_its_own_expression(Aggregation aggregation, string expected) =>
        Assert.Contains($"{expected} AS \"m\"", _renderer.Render(Dataset, Plan(measures: [Measure(aggregation, "x", "m")])).Sql);

    // ---- time grain ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(TimeGrain.Day, "day")]
    [InlineData(TimeGrain.Week, "week")]
    [InlineData(TimeGrain.Month, "month")]
    [InlineData(TimeGrain.Quarter, "quarter")]
    [InlineData(TimeGrain.Year, "year")]
    public void Grains_use_date_trunc(TimeGrain grain, string unit) =>
        Assert.Contains($"date_trunc('{unit}', \"at\") AS \"b\"",
            _renderer.Render(Dataset, Plan(dims: [Dim("at", "timestamp with time zone", grain, "b")])).Sql);

    [Fact]
    public void Plain_dates_are_cast_to_timestamp_so_the_session_time_zone_cannot_shift_the_bucket() =>
        Assert.Contains("date_trunc('month', \"d\"::timestamp)",
            _renderer.Render(Dataset, Plan(dims: [Dim("d", "date", TimeGrain.Month)])).Sql);

    [Fact]
    public void Timestamps_without_zone_are_not_cast() =>
        Assert.Contains("date_trunc('day', \"t\")",
            _renderer.Render(Dataset, Plan(dims: [Dim("t", "timestamp without time zone", TimeGrain.Day)])).Sql);

    // ---- filters ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(FilterOperator.Eq, "\"c\" = @databoard_p0")]
    [InlineData(FilterOperator.NotEq, "\"c\" <> @databoard_p0")]
    [InlineData(FilterOperator.Gt, "\"c\" > @databoard_p0")]
    [InlineData(FilterOperator.Gte, "\"c\" >= @databoard_p0")]
    [InlineData(FilterOperator.Lt, "\"c\" < @databoard_p0")]
    [InlineData(FilterOperator.Lte, "\"c\" <= @databoard_p0")]
    public void Comparison_operators(FilterOperator op, string expected)
    {
        var query = _renderer.Render(Dataset, Plan(measures: [Measure(Aggregation.Count, null, "n")], filters: [new("c", op, [5m])]));

        Assert.Contains("\nWHERE " + expected + "\n", query.Sql);
        Assert.Equal(new object[] { 5m }, query.Parameters);
    }

    [Fact]
    public void List_range_and_null_operators()
    {
        var query = _renderer.Render(Dataset, Plan(measures: [Measure(Aggregation.Count, null, "n")], filters:
        [
            new("a", FilterOperator.NotIn, [1m, 2m, 3m]),
            new("b", FilterOperator.Between, [10m, 20m]),
            new("c", FilterOperator.IsNull, []),
            new("d", FilterOperator.IsNotNull, []),
        ]));

        Assert.Contains(
            "\nWHERE \"a\" NOT IN (@databoard_p0, @databoard_p1, @databoard_p2) AND \"b\" BETWEEN @databoard_p3 AND @databoard_p4 " +
            "AND \"c\" IS NULL AND \"d\" IS NOT NULL\n",
            query.Sql);
        Assert.Equal(5, query.Parameters.Count);
    }

    [Fact]
    public void Text_search_escapes_wildcards_so_they_match_literally()
    {
        var query = _renderer.Render(Dataset, Plan(measures: [Measure(Aggregation.Count, null, "n")], filters:
        [
            new("notes", FilterOperator.Contains, ["50%_off\\"]),
            new("notes", FilterOperator.StartsWith, ["a_b"]),
        ]));

        Assert.Contains("\"notes\" ILIKE @databoard_p0 ESCAPE '\\' AND \"notes\" ILIKE @databoard_p1 ESCAPE '\\'", query.Sql);
        Assert.Equal("%50\\%\\_off\\\\%", query.Parameters[0]);
        Assert.Equal("a\\_b%", query.Parameters[1]);
    }

    [Fact]
    public void Filter_values_travel_as_parameters_and_never_appear_in_the_sql()
    {
        const string attack = "'; DROP TABLE sales; --";
        var query = _renderer.Render(Dataset, Plan(measures: [Measure(Aggregation.Count, null, "n")],
            filters: [new("region", FilterOperator.Eq, [attack]), new("note", FilterOperator.Contains, [attack])]));

        Assert.DoesNotContain("DROP", query.Sql);
        Assert.DoesNotContain("--", query.Sql.Replace(Dataset, ""));
        Assert.Equal(attack, query.Parameters[0]);
    }

    [Fact]
    public void Typed_values_are_passed_through_unchanged()
    {
        var guid = Guid.NewGuid();
        var date = new DateOnly(2026, 3, 5);
        var query = _renderer.Render(Dataset, Plan(measures: [Measure(Aggregation.Count, null, "n")], filters:
        [
            new("u", FilterOperator.Eq, [guid]), new("d", FilterOperator.Eq, [date]), new("p", FilterOperator.Eq, [true]),
        ]));

        Assert.Equal(new object[] { guid, date, true }, query.Parameters);
    }

    // ---- identifiers -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("a.b", "\"a.b\"")]
    [InlineData("x as y", "\"x as y\"")]
    [InlineData("is_active?", "\"is_active?\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("\"; DROP TABLE x; --", "\"\"\"; DROP TABLE x; --\"")]
    [InlineData("@databoard_p0", "\"@databoard_p0\"")]
    public void Identifiers_are_quoted_with_embedded_quotes_doubled(string name, string expected) =>
        Assert.Equal(expected, PostgresChartSqlRenderer.QuoteIdentifier(name));

    [Fact]
    public void Awkward_column_names_end_up_as_single_quoted_identifiers_everywhere()
    {
        const string weird = "we\"ird.col as x?";
        var plan = Plan(
            dims: [Dim(weird, alias: "d")],
            measures: [Measure(Aggregation.Sum, weird, "total")],
            filters: [new(weird, FilterOperator.Gt, [1m])]);

        var sql = _renderer.Render(Dataset, plan).Sql;

        var quoted = "\"we\"\"ird.col as x?\"";
        Assert.Contains($"SELECT {quoted} AS \"d\", SUM({quoted}) AS \"total\"", sql);
        Assert.Contains($"WHERE {quoted} > @databoard_p0", sql);
    }

    // ---- the dataset's own SQL is left alone -----------------------------------------------------------------

    [Fact]
    public void Dataset_sql_is_placed_verbatim_even_if_it_contains_question_marks_at_signs_or_a_trailing_comment()
    {
        const string tricky = "SELECT data ? 'k' AS has_k, data @> '{\"a\":1}' AS has_a, arr[1:2] AS slice FROM t -- trailing comment";

        var query = _renderer.Render(tricky, Plan(measures: [Measure(Aggregation.Count, null, "n")]));

        Assert.Contains($"FROM (\n{tricky}\n) AS \"databoard_dataset\"", query.Sql);
    }

    // ---- parameters ------------------------------------------------------------------------------------------

    [Fact]
    public void Parameter_placeholders_are_sequential_unique_and_match_the_parameter_list()
    {
        var revenue = Measure(Aggregation.Sum, "price", "revenue");
        var query = _renderer.Render(Dataset, Plan(
            dims: [Dim("region")],
            measures: [revenue],
            filters: [new("a", FilterOperator.In, ["x", "y"]), new("b", FilterOperator.Between, [1m, 2m])],
            having: [new(revenue, FilterOperator.Between, [5m, 6m])]));

        var used = Regex.Matches(query.Sql, @"@databoard_p(\d+)").Select(m => int.Parse(m.Groups[1].Value)).ToList();

        Assert.Equal(Enumerable.Range(0, query.Parameters.Count), used);
        Assert.Equal(6, query.Parameters.Count);
    }

    [Fact]
    public void Parameter_names_have_a_prefix_that_will_not_collide_with_user_sql() =>
        Assert.Equal("databoard_p3", Application.DataSources.ParameterizedQuery.Name(3));

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("50%", "50\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("c:\\dir", "c:\\\\dir")]
    [InlineData("%_\\", "\\%\\_\\\\")]
    public void Like_wildcards_are_escaped(string input, string expected) =>
        Assert.Equal(expected, SqlChartRendererBase.EscapeLike(input));

    [Fact]
    public void Limit_asks_for_one_extra_row_to_detect_truncation() =>
        Assert.EndsWith("\nLIMIT 1001", _renderer.Render(Dataset, Plan(measures: [Measure(Aggregation.Count, null, "n")], limit: 1000)).Sql);
}
