using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DataBoard.Application.DataSources;

namespace DataBoard.Application.Charts;

/// <summary>A query result kept for reuse. The SQL is kept beside it so that Editors can still be shown it.</summary>
public sealed record CachedResult(QueryPreview Result, string Sql, DateTime CachedUtc);

/// <summary>
/// Where finished chart results are kept for a while. Results are the same for every member of an organization (there
/// is no row-level security), so one entry serves all of them; the key says exactly which query it answers, so a
/// different filter, parameter, dataset version or data source can never be given someone else's answer.
/// </summary>
public interface IResultCache
{
    CachedResult? Get(string key);
    void Set(string key, CachedResult entry, TimeSpan timeToLive);
}

/// <summary>How a run uses the cache.</summary>
public enum CacheMode
{
    /// <summary>Always ask the database, and neither read nor write the cache (the chart editor's preview).</summary>
    Live,

    /// <summary>Use a stored result if there is one; otherwise run, and store it if the dataset keeps results.</summary>
    Use,

    /// <summary>Always ask the database, then store the result (for as long as the dataset keeps results, or the caller asks).</summary>
    Refresh,
}

/// <param name="Mode">How the cache is used.</param>
/// <param name="StoreFor">With <see cref="CacheMode.Refresh"/>: keep the result at least this long, even if the dataset keeps nothing.</param>
public sealed record CacheRequest(CacheMode Mode, TimeSpan? StoreFor = null)
{
    public static readonly CacheRequest Live = new(CacheMode.Live);
    public static readonly CacheRequest Use = new(CacheMode.Use);
}

public static class ResultCacheKey
{
    /// <summary>
    /// The key of one query's answer. It covers everything that can change the answer: whose data (organization,
    /// dataset and its version, data source and its connection), the exact SQL that runs, the values bound to it, and the row limit.
    /// </summary>
    /// <param name="cacheSeconds">
    /// The dataset's setting for how long results are kept. It is part of the key so that changing it, above all turning
    /// it off, takes effect at once: results stored under the old setting are simply never asked for again.
    /// </param>
    public static string For(
        Guid organizationId, Guid datasetId, DateTime datasetUpdatedUtc, int cacheSeconds, Guid dataSourceId, string encryptedConnection,
        ParameterizedQuery query, int limit)
    {
        var text = new StringBuilder();
        void Part(string value) => text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');

        Part(organizationId.ToString("N"));
        Part(datasetId.ToString("N"));
        Part(datasetUpdatedUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        Part(cacheSeconds.ToString(CultureInfo.InvariantCulture));
        Part(dataSourceId.ToString("N"));
        Part(Hash(encryptedConnection));
        Part(query.Sql);
        Part(limit.ToString(CultureInfo.InvariantCulture));
        foreach (var value in query.Parameters)
            Part(value is null ? "null" : $"{value.GetType().Name}={Convert.ToString(value, CultureInfo.InvariantCulture)}");

        return "chart:" + Hash(text.ToString());
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
