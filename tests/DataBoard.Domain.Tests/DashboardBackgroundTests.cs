using DataBoard.Domain.Dashboards;

namespace DataBoard.Domain.Tests;

public class DashboardBackgroundTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    private static Dashboard New() => Dashboard.Create(Guid.NewGuid(), "Sales", null, "{}", Guid.NewGuid(), Now);
    private const string Key = "0123456789abcdef0123456789abcdef.png";

    [Fact]
    public void Has_no_picture_to_begin_with_and_shows_one_filling_the_page_at_full_strength()
    {
        var d = New();

        Assert.Null(d.BackgroundImageKey);
        Assert.Equal("cover", d.BackgroundFit);
        Assert.Equal(100, d.BackgroundOpacity);
    }

    [Fact]
    public void Sets_a_picture_and_how_it_is_shown_without_counting_as_an_edit()
    {
        var d = New();
        var updated = d.UpdatedUtc;

        d.SetBackground(Key, "tile", 40);

        Assert.Equal(Key, d.BackgroundImageKey);
        Assert.Equal("tile", d.BackgroundFit);
        Assert.Equal(40, d.BackgroundOpacity);
        Assert.Equal(updated, d.UpdatedUtc);
    }

    [Theory]
    [InlineData("cover")]
    [InlineData("contain")]
    [InlineData("tile")]
    [InlineData("center")]
    public void Takes_each_way_of_showing_the_picture(string fit)
    {
        var d = New();

        d.SetBackground(Key, fit, 100);

        Assert.Equal(fit, d.BackgroundFit);
    }

    [Theory]
    [InlineData("stretch", 100)]
    [InlineData("cover", 9)]
    [InlineData("cover", 101)]
    [InlineData("", 100)]
    public void Refuses_what_is_out_of_range_and_keeps_what_it_had(string fit, int opacity)
    {
        var d = New();
        d.SetBackground(Key, "contain", 70);

        Assert.Throws<ArgumentException>(() => d.SetBackground(Key, fit, opacity));

        Assert.Equal("contain", d.BackgroundFit);
        Assert.Equal(70, d.BackgroundOpacity);
    }
    [Fact]
    public void Needs_a_picture_with_a_sensible_key()
    {
        var d = New();

        Assert.Throws<ArgumentException>(() => d.SetBackground("", "cover", 100));
        Assert.Throws<ArgumentException>(() => d.SetBackground(new string('x', Dashboard.MaxBackgroundKeyLength + 1), "cover", 100));
        Assert.Null(d.BackgroundImageKey);
    }

    [Fact]
    public void Clearing_puts_everything_back()
    {
        var d = New();
        d.SetBackground(Key, "tile", 30);

        d.ClearBackground();

        Assert.Null(d.BackgroundImageKey);
        Assert.Equal("cover", d.BackgroundFit);
        Assert.Equal(100, d.BackgroundOpacity);
    }
}
