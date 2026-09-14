using DataBoard.Domain.Dashboards;

namespace DataBoard.Domain.Tests;

public class DashboardRefreshTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private static Dashboard New() => Dashboard.Create(Guid.NewGuid(), "Sales", null, "{}", Guid.NewGuid(), Now);

    [Fact]
    public void Is_not_refreshed_on_a_schedule_to_begin_with()
    {
        var d = New();

        Assert.Null(d.RefreshIntervalMinutes);
        Assert.Null(d.NextRefreshUtc);
        Assert.Null(d.LastRefreshUtc);
        Assert.Equal(0, d.LastRefreshFailures);
    }

    [Theory]
    [InlineData(Dashboard.MinRefreshMinutes)]
    [InlineData(60)]
    [InlineData(Dashboard.MaxRefreshMinutes)]
    public void A_schedule_makes_the_first_refresh_due_at_once(int minutes)
    {
        var d = New();

        d.SetRefreshInterval(minutes, Now);

        Assert.Equal(minutes, d.RefreshIntervalMinutes);
        Assert.Equal(Now, d.NextRefreshUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(Dashboard.MinRefreshMinutes - 1)]
    [InlineData(Dashboard.MaxRefreshMinutes + 1)]
    [InlineData(-5)]
    public void Refuses_an_interval_that_is_too_short_or_too_long_and_keeps_the_old_one(int minutes)
    {
        var d = New();
        d.SetRefreshInterval(30, Now);

        Assert.Throws<ArgumentException>(() => d.SetRefreshInterval(minutes, Now.AddMinutes(1)));

        Assert.Equal(30, d.RefreshIntervalMinutes);
        Assert.Equal(Now, d.NextRefreshUtc);
    }

    [Fact]
    public void Turning_the_schedule_off_clears_when_the_next_one_is_due_but_keeps_the_record_of_the_last()
    {
        var d = New();
        d.SetRefreshInterval(15, Now);
        d.RecordRefresh(Now.AddMinutes(1), 0, null);

        d.SetRefreshInterval(null, Now.AddMinutes(2));

        Assert.Null(d.RefreshIntervalMinutes);
        Assert.Null(d.NextRefreshUtc);
        Assert.Equal(Now.AddMinutes(1), d.LastRefreshUtc);
    }

    [Fact]
    public void A_refresh_is_recorded_and_the_next_one_is_due_an_interval_later()
    {
        var d = New();
        d.SetRefreshInterval(15, Now);

        d.RecordRefresh(Now.AddMinutes(1), 0, null);

        Assert.Equal(Now.AddMinutes(1), d.LastRefreshUtc);
        Assert.Equal(Now.AddMinutes(16), d.NextRefreshUtc);
        Assert.Equal(0, d.LastRefreshFailures);
        Assert.Null(d.LastRefreshMessage);
    }

    [Fact]
    public void A_refresh_that_failed_keeps_how_many_charts_and_the_first_problem()
    {
        var d = New();
        d.SetRefreshInterval(15, Now);

        d.RecordRefresh(Now, 2, "  'price' is not a column of this dataset.  ");

        Assert.Equal(2, d.LastRefreshFailures);
        Assert.Equal("'price' is not a column of this dataset.", d.LastRefreshMessage);
        Assert.Equal(Now.AddMinutes(15), d.NextRefreshUtc); // it is tried again at the next interval, not at once
    }

    [Fact]
    public void A_long_message_is_cut_short()
    {
        var d = New();

        d.RecordRefresh(Now, 1, new string('x', 1_000));

        Assert.Equal(Dashboard.MaxRefreshMessageLength, d.LastRefreshMessage!.Length);
        Assert.EndsWith("…", d.LastRefreshMessage);
    }

    [Fact]
    public void A_good_refresh_forgets_the_last_problem()
    {
        var d = New();
        d.RecordRefresh(Now, 1, "boom");

        d.RecordRefresh(Now.AddMinutes(15), 0, "ignored: nothing failed");

        Assert.Equal(0, d.LastRefreshFailures);
        Assert.Null(d.LastRefreshMessage);
    }

    [Fact]
    public void Without_a_schedule_a_record_leaves_nothing_due()
    {
        var d = New();

        d.RecordRefresh(Now, 0, null);

        Assert.Null(d.NextRefreshUtc);
    }

    [Fact]
    public void Cannot_record_a_negative_number_of_failures() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New().RecordRefresh(Now, -1, null));

    [Fact]
    public void Editing_the_dashboard_keeps_the_schedule_and_the_record_and_does_not_count_the_schedule_as_an_edit()
    {
        var d = New();
        d.SetRefreshInterval(15, Now);
        d.RecordRefresh(Now, 1, "boom");
        var updated = d.UpdatedUtc;

        d.Update("Sales 2", null, """{"widgets":[]}""", Now.AddHours(1));

        Assert.Equal(15, d.RefreshIntervalMinutes);
        Assert.Equal("boom", d.LastRefreshMessage);
        Assert.NotEqual(updated, d.UpdatedUtc); // the edit counts ...
        var before = d.UpdatedUtc;
        d.SetRefreshInterval(60, Now.AddHours(2));
        d.RecordRefresh(Now.AddHours(2), 0, null);
        Assert.Equal(before, d.UpdatedUtc); // ... the schedule and its record do not
    }
}
