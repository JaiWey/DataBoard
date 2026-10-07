using DataBoard.Application.Charts;
using DataBoard.Application.DataSources;

namespace DataBoard.Application.Tests;

public class GrainLabelsTests
{
    private static ChartQueryPlan PlanOf(params PlannedDimension[] dimensions) =>
        new(dimensions, [new PlannedMeasure(Aggregation.Count, null, "count")], [], [], [], 100);

    private static QueryPreview Result(object? first, object? second = null) =>
        new([new("d", "timestamp"), new("e", "timestamp"), new("count", "bigint")], [[first, second, 3L]], false);

    [Theory]
    [InlineData(TimeGrain.Year, "2026")]
    [InlineData(TimeGrain.Quarter, "2026-Q1")]
    [InlineData(TimeGrain.Month, "2026-03")]
    [InlineData(TimeGrain.Week, "2026-03-02")]
    [InlineData(TimeGrain.Day, "2026-03-02")]
    public void A_date_becomes_the_name_of_its_period(TimeGrain grain, string expected)
    {
        var plan = PlanOf(new PlannedDimension("d", "timestamp", grain, "d"));

        var result = GrainLabels.Apply(Result(new DateTime(2026, 3, 2)), plan);

        Assert.Equal(expected, result.Rows[0][0]);
        Assert.Equal("text", result.Columns[0].DataType);
        Assert.Equal(3L, result.Rows[0][2]); // the measures are left alone
    }

    [Fact]
    public void Dates_without_a_time_and_text_dates_get_the_same_label()
    {
        var plan = PlanOf(new PlannedDimension("d", "date", TimeGrain.Month, "d"));

        Assert.Equal("2026-03", GrainLabels.Apply(Result(new DateOnly(2026, 3, 1)), plan).Rows[0][0]);
        Assert.Equal("2026-03", GrainLabels.Apply(Result("2026-03-01 00:00:00"), plan).Rows[0][0]);
        Assert.Equal("2026-03", GrainLabels.Apply(Result(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)), plan).Rows[0][0]);
    }

    [Fact]
    public void Only_dimensions_with_a_grain_are_changed_and_an_empty_value_stays_empty()
    {
        var plan = PlanOf(
            new PlannedDimension("d", "timestamp", TimeGrain.Year, "d"),
            new PlannedDimension("e", "timestamp", null, "e"));
        var exact = new DateTime(2026, 3, 2, 10, 30, 0);

        var result = GrainLabels.Apply(Result(null, exact), plan);

        Assert.Null(result.Rows[0][0]);
        Assert.Equal(exact, result.Rows[0][1]);
        Assert.Equal("timestamp", result.Columns[1].DataType);
    }

    [Fact]
    public void A_result_without_a_grain_is_returned_as_it_is()
    {
        var plan = PlanOf(new PlannedDimension("d", "timestamp", null, "d"));
        var result = Result(new DateTime(2026, 3, 2));

        Assert.Same(result, GrainLabels.Apply(result, plan));
    }
}
