using System.Globalization;
using DataBoard.Application.DataSources;

namespace DataBoard.Application.Charts;

/// <summary>
/// A date grouped by a grain comes back from the database as the first moment of its period (2026-03-01 00:00:00 for
/// March 2026). That is what a chart should not show on its axis, so such a dimension is turned into the period's
/// name: "2026", "2026-Q1", "2026-03" or the day "2026-03-05". These sort the same way as the dates did, so the order
/// the database gave stays right.
/// </summary>
public static class GrainLabels
{
    public static QueryPreview Apply(QueryPreview result, ChartQueryPlan plan)
    {
        var grained = plan.Dimensions.Select((d, i) => (d.Grain, Index: i)).Where(x => x.Grain is not null).ToList();
        if (grained.Count == 0) return result;

        var columns = result.Columns.ToList();
        foreach (var (_, index) in grained)
            if (index < columns.Count) columns[index] = columns[index] with { DataType = "text" };

        var rows = result.Rows.Select(row =>
        {
            var copy = (object?[])row.Clone();
            foreach (var (grain, index) in grained)
                if (index < copy.Length) copy[index] = Label(copy[index], grain!.Value);
            return copy;
        }).ToList();

        return result with { Columns = columns, Rows = rows };
    }

    public static string Format(DateTime value, TimeGrain grain) => grain switch
    {
        TimeGrain.Year => value.ToString("yyyy", CultureInfo.InvariantCulture),
        TimeGrain.Quarter => $"{value.Year:0000}-Q{(value.Month - 1) / 3 + 1}",
        TimeGrain.Month => value.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        _ => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), // a day, or the first day of a week
    };

    private static object? Label(object? value, TimeGrain grain) => value switch
    {
        null => null,
        DateTime d => Format(d, grain),
        DateOnly d => Format(d.ToDateTime(TimeOnly.MinValue), grain),
        DateTimeOffset o => Format(o.UtcDateTime, grain),
        // SQLite has no date type and hands the period back as text.
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) => Format(d, grain),
        _ => value,
    };
}
