using DataBoard.Application.Charts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DataBoard.Infrastructure.Services;

public sealed class ResultCacheOptions
{
    public const string SectionName = "Caching";

    /// <summary>
    /// How much the cache may hold, counted in cells (rows times columns) of all stored results together. When it is
    /// full a quarter of it is freed, the entries least recently used going first. A single result bigger than this is
    /// simply not stored.
    /// </summary>
    public int MaxCells { get; set; } = 5_000_000;
}

/// <summary>
/// Keeps chart results in this server's memory, within a size limit. Several servers each keep their own; a shared
/// cache (Redis) can replace this class behind <see cref="IResultCache"/> without touching anything else.
/// </summary>
public sealed class MemoryResultCache : IResultCache, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly int _maxCells;

    public MemoryResultCache(IOptions<ResultCacheOptions> options)
    {
        _maxCells = Math.Max(1, options.Value.MaxCells);
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = _maxCells, CompactionPercentage = 0.25 });
    }

    public CachedResult? Get(string key) => _cache.TryGetValue(key, out CachedResult? entry) ? entry : null;

    public void Set(string key, CachedResult entry, TimeSpan timeToLive)
    {
        if (timeToLive <= TimeSpan.Zero) return;

        var cells = Math.Max(1, entry.Result.Rows.Count * Math.Max(1, entry.Result.Columns.Count));
        if (cells > _maxCells) return; // too big to keep without pushing everything else out

        // When the cache is full, the result that arrives is turned away and the cache starts dropping the entries used
        // least recently, a quarter of its size at a time (see the options). The next result then finds room.
        _cache.Set(key, entry, new MemoryCacheEntryOptions { Size = cells, AbsoluteExpirationRelativeToNow = timeToLive });
    }

    public void Dispose() => _cache.Dispose();
}
