using DataBoard.Application.Charts;
using DataBoard.Application.DataSources;
using DataBoard.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace DataBoard.Infrastructure.Tests;

public class MemoryResultCacheTests
{
    private static MemoryResultCache Cache(int maxCells = 1_000) => new(Options.Create(new ResultCacheOptions { MaxCells = maxCells }));

    /// <summary>A result of the given size: <paramref name="rows"/> rows of two columns.</summary>
    private static CachedResult Result(int rows, string sql = "SELECT 1") =>
        new(new QueryPreview([new("a", "text"), new("b", "bigint")], Enumerable.Range(0, rows).Select(i => new object?[] { "x", (long)i }).ToList(), false),
            sql, new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Gives_back_what_was_stored()
    {
        using var cache = Cache();
        var entry = Result(3, "SELECT stored");

        cache.Set("k", entry, TimeSpan.FromMinutes(5));

        Assert.Same(entry, cache.Get("k"));
    }

    [Fact]
    public void Has_nothing_for_a_key_it_was_never_given() =>
        Assert.Null(Cache().Get("never"));

    [Fact]
    public async Task Forgets_an_entry_when_its_time_is_up()
    {
        using var cache = Cache();
        cache.Set("short", Result(1), TimeSpan.FromMilliseconds(150));
        cache.Set("long", Result(1), TimeSpan.FromMinutes(5));

        await Task.Delay(400);

        Assert.Null(cache.Get("short"));
        Assert.NotNull(cache.Get("long"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Stores_nothing_for_no_time_at_all(int seconds)
    {
        using var cache = Cache();

        cache.Set("k", Result(1), TimeSpan.FromSeconds(seconds));

        Assert.Null(cache.Get("k"));
    }

    [Fact]
    public void A_newer_answer_replaces_an_older_one_under_the_same_key()
    {
        using var cache = Cache();
        cache.Set("k", Result(1, "old"), TimeSpan.FromMinutes(5));
        var newer = Result(1, "new");

        cache.Set("k", newer, TimeSpan.FromMinutes(5));

        Assert.Same(newer, cache.Get("k"));
    }

    [Fact]
    public void Does_not_keep_a_result_that_is_bigger_than_the_whole_cache_and_does_not_push_others_out_for_it()
    {
        using var cache = Cache(maxCells: 100);
        var small = Result(10); // 20 cells
        cache.Set("small", small, TimeSpan.FromMinutes(5));

        cache.Set("huge", Result(1_000), TimeSpan.FromMinutes(5)); // 2000 cells

        Assert.Null(cache.Get("huge"));
        Assert.Same(small, cache.Get("small"));
    }

    [Fact]
    public void Counts_the_size_of_a_result_in_cells_and_makes_room_by_dropping_what_was_used_least_recently()
    {
        using var cache = Cache(maxCells: 100);
        // The cache orders use by the clock, so the steps are spaced out enough for it to tell them apart.
        cache.Set("a", Result(20), TimeSpan.FromMinutes(5)); // 40 cells
        Thread.Sleep(30);
        cache.Set("b", Result(20), TimeSpan.FromMinutes(5)); // 80 cells in all
        Thread.Sleep(30);
        Assert.NotNull(cache.Get("a")); // a was used more recently than b
        Thread.Sleep(30);

        cache.Set("c", Result(20), TimeSpan.FromMinutes(5)); // needs 40 more: this one is turned away and trimming starts

        Thread.Sleep(300); // the trimming runs in the background (and looking at b would count as using it, so do not look yet)
        cache.Set("c", Result(20), TimeSpan.FromMinutes(5)); // there is room now

        Assert.Null(cache.Get("b")); // the least recently used went
        Assert.NotNull(cache.Get("a"));
        Assert.NotNull(cache.Get("c"));
    }

    [Fact]
    public void Keeps_an_empty_result_too_as_one_cell()
    {
        using var cache = Cache(maxCells: 5);

        cache.Set("empty", Result(0), TimeSpan.FromMinutes(5));

        Assert.NotNull(cache.Get("empty")); // an empty answer is still an answer worth not asking for again
    }
}
