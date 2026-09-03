using System.Text.Json;
using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Domain.Datasets;

namespace DataBoard.Application.Tests;

public class ChartQueryPlannerTests
{
    private static readonly DatasetColumn[] Columns =
    [
        new("region", "text", ColumnRole.Dimension),
        new("product", "character varying(100)", ColumnRole.Dimension),
        new("quantity", "integer", ColumnRole.Measure),
        new("price", "numeric(10, 2)", ColumnRole.Measure),
        new("ordered_on", "date", ColumnRole.Dimension),
        new("created_at", "timestamp with time zone", ColumnRole.Dimension),
        new("local_at", "timestamp without time zone", ColumnRole.Dimension),
        new("paid", "boolean", ColumnRole.Dimension),
        new("customer", "uuid", ColumnRole.Dimension),
        new("data", "jsonb", ColumnRole.Dimension),
        new("we\"ird.col as x?", "integer", ColumnRole.Measure),
    ];

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static ChartQueryPlan Plan(ChartQuery q) => ChartQueryPlanner.Plan(Columns, q);

    private static ValidationException Rejects(ChartQuery q) =>
        Assert.Throws<ValidationException>(() => Plan(q));

    private static ChartQuery Sum(params FilterSpec[] filters) =>
        new(Measures: [new MeasureSpec(Aggregation.Sum, "quantity")], Filters: filters);

    // ---- shape -----------------------------------------------------------------------------------------------

    [Fact]
    public void An_empty_query_is_rejected() => Rejects(new ChartQuery());

    [Fact]
    public void Dimensions_and_measures_get_default_aliases_and_a_default_sort()
    {
        var plan = Plan(new ChartQuery(
            Dimensions: [new("region"), new("ordered_on", TimeGrain.Month)],
            Measures:
            [
                new(Aggregation.Sum, "price"), new(Aggregation.Count), new(Aggregation.CountDistinct, "product"),
                new(Aggregation.Avg, "quantity", "avg_qty"),
            ]));

        Assert.Equal(["region", "ordered_on_month"], plan.Dimensions.Select(d => d.Alias));
        Assert.Equal(["sum_price", "count", "count_distinct_product", "avg_qty"], plan.Measures.Select(m => m.Alias));
        Assert.Equal([(1, SortDirection.Asc), (2, SortDirection.Asc)], plan.OrderBy.Select(o => (o.Position, o.Direction)));
        Assert.Equal(ChartQueryPlanner.DefaultLimit, plan.Limit);
    }

    [Fact]
    public void A_query_with_only_measures_has_no_default_sort() =>
        Assert.Empty(Plan(new ChartQuery(Measures: [new(Aggregation.Count)])).OrderBy);

    [Fact]
    public void Order_by_refers_to_aliases_by_position_dimensions_first()
    {
        var plan = Plan(new ChartQuery(
            Dimensions: [new("region"), new("product")],
            Measures: [new(Aggregation.Sum, "price", "revenue")],
            OrderBy: [new("revenue", SortDirection.Desc), new("PRODUCT")]));

        Assert.Equal([(3, SortDirection.Desc), (2, SortDirection.Asc)], plan.OrderBy.Select(o => (o.Position, o.Direction)));
    }

    // ---- columns and aliases ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("nope")]
    [InlineData("REGION")] // names are exact
    [InlineData("region; DROP TABLE x")]
    [InlineData("")]
    public void Unknown_columns_are_rejected_everywhere(string name)
    {
        Rejects(new ChartQuery(Dimensions: [new(name)]));
        Rejects(new ChartQuery(Measures: [new(Aggregation.Count, name)]));
        Rejects(Sum(new FilterSpec(name, FilterOperator.IsNull)));
    }

    [Fact]
    public void Columns_with_awkward_names_are_fine_because_they_are_only_ever_quoted()
    {
        var plan = Plan(new ChartQuery(Measures: [new(Aggregation.Sum, "we\"ird.col as x?", "total")]));

        Assert.Equal("we\"ird.col as x?", plan.Measures.Single().Column);
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("a b")]
    [InlineData("a\"b")]
    [InlineData("a;b")]
    [InlineData("a-b")]
    [InlineData("x\" FROM secrets --")]
    [InlineData("")]
    public void Explicit_aliases_must_be_plain_identifiers(string alias)
    {
        Rejects(new ChartQuery(Dimensions: [new("region", Alias: alias)]));
        Rejects(new ChartQuery(Measures: [new(Aggregation.Count, Alias: alias)]));
    }

    [Fact]
    public void An_alias_longer_than_the_database_allows_is_rejected()
    {
        Rejects(new ChartQuery(Measures: [new(Aggregation.Count, Alias: new string('a', 64))]));
        Plan(new ChartQuery(Measures: [new(Aggregation.Count, Alias: new string('a', 63))]));
    }

    [Fact]
    public void Result_names_must_be_unique_ignoring_case()
    {
        var error = Rejects(new ChartQuery(
            Dimensions: [new("region")],
            Measures: [new(Aggregation.Count, Alias: "REGION")]));

        Assert.Contains("more than once", error.Errors["Query"][0]);
    }

    // ---- aggregations and grains -----------------------------------------------------------------------------

    [Theory]
    [InlineData(Aggregation.Sum, "region")]
    [InlineData(Aggregation.Avg, "paid")]
    [InlineData(Aggregation.Sum, "ordered_on")]
    [InlineData(Aggregation.Min, "paid")]
    [InlineData(Aggregation.Max, "data")]
    [InlineData(Aggregation.Sum, null)]
    [InlineData(Aggregation.CountDistinct, null)]
    public void Aggregations_must_suit_the_column(Aggregation aggregation, string? column) =>
        Rejects(new ChartQuery(Measures: [new(aggregation, column)]));

    [Theory]
    [InlineData(Aggregation.Sum, "quantity")]
    [InlineData(Aggregation.Avg, "price")]
    [InlineData(Aggregation.Min, "ordered_on")]
    [InlineData(Aggregation.Max, "region")]
    [InlineData(Aggregation.Count, "data")]
    [InlineData(Aggregation.Count, null)]
    [InlineData(Aggregation.CountDistinct, "customer")]
    public void Valid_aggregations_are_accepted(Aggregation aggregation, string? column) =>
        Plan(new ChartQuery(Measures: [new(aggregation, column)]));

    [Fact]
    public void Unknown_aggregation_values_are_rejected() =>
        Rejects(new ChartQuery(Measures: [new((Aggregation)99, "quantity")]));

    [Theory]
    [InlineData("ordered_on")]
    [InlineData("created_at")]
    [InlineData("local_at")]
    public void Time_grains_work_on_dates_and_timestamps(string column) =>
        Assert.Equal(TimeGrain.Week, Plan(new ChartQuery(Dimensions: [new(column, TimeGrain.Week)])).Dimensions.Single().Grain);

    [Theory]
    [InlineData("region")]
    [InlineData("quantity")]
    [InlineData("paid")]
    public void Time_grains_are_rejected_on_other_columns(string column) =>
        Rejects(new ChartQuery(Dimensions: [new(column, TimeGrain.Day)]));

    // ---- filters ---------------------------------------------------------------------------------------------

    [Fact]
    public void Values_are_converted_to_the_columns_type()
    {
        var plan = Plan(new ChartQuery(Measures: [new(Aggregation.Count)], Filters:
        [
            new("quantity", FilterOperator.Gte, J("3")),
            new("price", FilterOperator.Lt, J("\"12.50\"")), // numeric strings are accepted
            new("paid", FilterOperator.Eq, J("true")),
            new("region", FilterOperator.Eq, J("\"Auckland\"")),
            new("customer", FilterOperator.Eq, J("\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"")),
            new("ordered_on", FilterOperator.Eq, J("\"2026-03-05\"")),
        ]));

        Assert.Equal(
            [typeof(decimal), typeof(decimal), typeof(bool), typeof(string), typeof(Guid), typeof(DateOnly)],
            plan.Filters.Select(f => f.Values.Single().GetType()));
        Assert.Equal(12.50m, plan.Filters[1].Values.Single());
    }

    [Fact]
    public void Timestamps_are_sent_in_the_kind_the_database_column_expects()
    {
        var plan = Plan(new ChartQuery(Measures: [new(Aggregation.Count)], Filters:
        [
            new("created_at", FilterOperator.Gte, J("\"2026-03-05T10:30:00+13:00\"")),
            new("local_at", FilterOperator.Gte, J("\"2026-03-05T10:30:00\"")),
        ]));

        var withZone = Assert.IsType<DateTime>(plan.Filters[0].Values.Single());
        var withoutZone = Assert.IsType<DateTime>(plan.Filters[1].Values.Single());
        Assert.Equal(DateTimeKind.Utc, withZone.Kind);
        Assert.Equal(new DateTime(2026, 3, 4, 21, 30, 0), withZone); // converted to UTC
        Assert.Equal(DateTimeKind.Unspecified, withoutZone.Kind);   // so it stays a "timestamp", not a "timestamptz"
        Assert.Equal(new DateTime(2026, 3, 5, 10, 30, 0), withoutZone);
    }

    [Theory]
    [InlineData("quantity", "\"abc\"")]
    [InlineData("quantity", "true")]
    [InlineData("quantity", "1e400")]
    [InlineData("paid", "\"yes\"")]
    [InlineData("paid", "1")]
    [InlineData("region", "5")]
    [InlineData("customer", "\"not-a-guid\"")]
    [InlineData("ordered_on", "\"05/03/2026\"")]
    [InlineData("ordered_on", "\"2026-03-05T10:00:00Z\"")]
    [InlineData("created_at", "\"yesterday\"")]
    public void Values_that_do_not_fit_the_column_are_rejected(string column, string json) =>
        Rejects(Sum(new FilterSpec(column, FilterOperator.Eq, J(json))));

    [Theory]
    [InlineData("paid", FilterOperator.Gt)]
    [InlineData("customer", FilterOperator.Lt)]
    [InlineData("data", FilterOperator.Eq)]
    [InlineData("quantity", FilterOperator.Contains)]
    [InlineData("ordered_on", FilterOperator.StartsWith)]
    [InlineData("data", FilterOperator.In)]
    public void Operators_must_suit_the_column(string column, FilterOperator op) =>
        Rejects(Sum(new FilterSpec(column, op, J("\"x\""), [J("\"x\"")])));

    [Fact]
    public void Null_checks_work_on_every_column_type()
    {
        foreach (var column in Columns)
        {
            var plan = Plan(Sum(new FilterSpec(column.Name, FilterOperator.IsNull), new FilterSpec(column.Name, FilterOperator.IsNotNull)));
            Assert.All(plan.Filters, f => Assert.Empty(f.Values));
        }
    }

    [Fact]
    public void Missing_or_null_values_are_rejected()
    {
        Rejects(Sum(new FilterSpec("quantity", FilterOperator.Eq)));
        Rejects(Sum(new FilterSpec("quantity", FilterOperator.Eq, J("null"))));
    }

    [Fact]
    public void List_and_range_filters_check_how_many_values_they_get()
    {
        Rejects(Sum(new FilterSpec("region", FilterOperator.In, Values: [])));
        Rejects(Sum(new FilterSpec("region", FilterOperator.In)));
        Rejects(Sum(new FilterSpec("region", FilterOperator.In,
            Values: Enumerable.Range(0, ChartQueryPlanner.MaxListValues + 1).Select(i => J($"\"v{i}\"")).ToList())));
        Rejects(Sum(new FilterSpec("quantity", FilterOperator.Between, Values: [J("1")])));
        Rejects(Sum(new FilterSpec("quantity", FilterOperator.Between, Values: [J("1"), J("2"), J("3")])));

        var plan = Plan(Sum(
            new FilterSpec("region", FilterOperator.In, Values: [J("\"a\""), J("\"b\"")]),
            new FilterSpec("quantity", FilterOperator.Between, Values: [J("1"), J("5")])));
        Assert.Equal([2, 2], plan.Filters.Select(f => f.Values.Count));
    }

    [Fact]
    public void Text_search_needs_a_reasonable_pattern()
    {
        Rejects(Sum(new FilterSpec("region", FilterOperator.Contains, J("\"\""))));
        Rejects(Sum(new FilterSpec("region", FilterOperator.Contains, J($"\"{new string('a', ChartQueryPlanner.MaxPatternLength + 1)}\""))));
        Plan(Sum(new FilterSpec("region", FilterOperator.Contains, J("\"50%\""))));
    }

    [Fact]
    public void Too_many_filters_dimensions_or_measures_are_rejected()
    {
        Rejects(Sum(Enumerable.Range(0, ChartQueryPlanner.MaxFilters + 1).Select(_ => new FilterSpec("region", FilterOperator.IsNull)).ToArray()));
        Rejects(new ChartQuery(Dimensions: Enumerable.Range(0, ChartQueryPlanner.MaxDimensions + 1).Select(_ => new DimensionSpec("region")).ToList()));
        Rejects(new ChartQuery(Measures: Enumerable.Range(0, ChartQueryPlanner.MaxMeasures + 1).Select(i => new MeasureSpec(Aggregation.Count, Alias: $"m{i}")).ToList()));
    }

    // ---- measure filters (HAVING) ----------------------------------------------------------------------------

    private static ChartQuery Having(params FilterSpec[] filters) => new(
        Dimensions: [new("region")],
        Measures: [new(Aggregation.Sum, "price", "revenue"), new(Aggregation.Max, "region", "last_region"), new(Aggregation.Count, Alias: "n")],
        MeasureFilters: filters);

    [Fact]
    public void Measure_filters_refer_to_measures_by_alias_and_use_numbers()
    {
        var plan = Plan(Having(new FilterSpec("REVENUE", FilterOperator.Gt, J("1000")), new FilterSpec("n", FilterOperator.Between, Values: [J("1"), J("9")])));

        Assert.Equal(["revenue", "n"], plan.MeasureFilters.Select(f => f.Measure.Alias));
        Assert.Equal([1000m], plan.MeasureFilters[0].Values);
        Assert.Equal([1m, 9m], plan.MeasureFilters[1].Values);
    }

    [Fact]
    public void Measure_filters_reject_unknown_measures_non_numeric_results_and_odd_operators()
    {
        Rejects(Having(new FilterSpec("region", FilterOperator.Gt, J("1")))); // a dimension, not a measure
        Rejects(Having(new FilterSpec("last_region", FilterOperator.Gt, J("1")))); // max of text is not a number
        Rejects(Having(new FilterSpec("revenue", FilterOperator.Contains, J("\"x\""))));
        Rejects(Having(new FilterSpec("revenue", FilterOperator.IsNull)));
        Rejects(Having(new FilterSpec("revenue", FilterOperator.Gt, J("\"abc\""))));
    }

    // ---- ordering and limit ------------------------------------------------------------------------------------

    [Fact]
    public void Ordering_by_unknown_or_repeated_fields_is_rejected()
    {
        var q = new ChartQuery(Dimensions: [new("region")], Measures: [new(Aggregation.Count)]);

        Rejects(q with { OrderBy = [new("nope")] });
        Rejects(q with { OrderBy = [new("region"), new("REGION", SortDirection.Desc)] });
        Rejects(q with { OrderBy = [new("region", (SortDirection)9)] });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ChartQueryPlanner.MaxLimit + 1)]
    public void Limits_outside_the_allowed_range_are_rejected(int limit) =>
        Rejects(new ChartQuery(Measures: [new(Aggregation.Count)], Limit: limit));

    [Theory]
    [InlineData(1)]
    [InlineData(ChartQueryPlanner.MaxLimit)]
    public void Limits_inside_the_range_are_accepted(int limit) =>
        Assert.Equal(limit, Plan(new ChartQuery(Measures: [new(Aggregation.Count)], Limit: limit)).Limit);

    [Fact]
    public void Every_problem_is_reported_at_once_with_its_location()
    {
        var error = Rejects(new ChartQuery(
            Dimensions: [new("nope")],
            Measures: [new(Aggregation.Sum, "region")],
            Filters: [new("quantity", FilterOperator.Eq, J("\"x\""))],
            Limit: 0));

        Assert.Contains("Dimensions[0]", error.Errors.Keys);
        Assert.Contains("Measures[0]", error.Errors.Keys);
        Assert.Contains("Filters[0]", error.Errors.Keys);
        Assert.Contains("Limit", error.Errors.Keys);
    }
}

public class ColumnKindsTests
{
    [Theory]
    [InlineData("integer", ColumnKind.Numeric)]
    [InlineData("numeric(10, 2)", ColumnKind.Numeric)]
    [InlineData("double precision", ColumnKind.Numeric)]
    [InlineData("text", ColumnKind.Text)]
    [InlineData("character varying(256)", ColumnKind.Text)]
    [InlineData("character(5)", ColumnKind.Text)]
    [InlineData("boolean", ColumnKind.Boolean)]
    [InlineData("uuid", ColumnKind.Uuid)]
    [InlineData("date", ColumnKind.Date)]
    [InlineData("timestamp without time zone", ColumnKind.Timestamp)]
    [InlineData("timestamp with time zone", ColumnKind.TimestampTz)]
    [InlineData("jsonb", ColumnKind.Other)]
    [InlineData("integer[]", ColumnKind.Other)]
    [InlineData("bytea", ColumnKind.Other)]
    [InlineData("interval", ColumnKind.Other)]
    [InlineData("money", ColumnKind.Other)]
    public void Database_types_map_to_kinds(string dataType, ColumnKind expected) =>
        Assert.Equal(expected, ColumnKinds.Of(dataType));
}
