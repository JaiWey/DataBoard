using DataBoard.Application.Charts;
using DataBoard.Domain.DataSources;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// Writes SQLite for a validated <see cref="ChartQueryPlan"/> (the data of an uploaded CSV). Dates are kept as text in the
/// form SQLite's date functions read ("2026-03-05", "2026-03-05 14:30:00"), so a time grain is a date function on that text.
/// </summary>
internal sealed class SqliteChartSqlRenderer : SqlChartRendererBase
{
    public override DataSourceType Type => DataSourceType.Csv;

    protected override string Quote(string identifier) => PostgresChartSqlRenderer.QuoteIdentifier(identifier);

    protected override string DimensionExpression(PlannedDimension d)
    {
        var column = Quote(d.Column);
        return d.Grain switch
        {
            null => column,
            TimeGrain.Day => $"date({column})",
            TimeGrain.Month => $"date({column}, 'start of month')",
            TimeGrain.Year => $"date({column}, 'start of year')",
            // The first day of the quarter: the month's first day, less the months since the quarter began.
            TimeGrain.Quarter => $"date({column}, 'start of month', '-' || ((CAST(strftime('%m', {column}) AS integer) - 1) % 3) || ' months')",
            // The Monday of the week (strftime's %w counts Sunday as 0), as PostgreSQL's date_trunc does.
            TimeGrain.Week => $"date({column}, '-' || ((CAST(strftime('%w', {column}) AS integer) + 6) % 7) || ' days')",
            _ => throw new ArgumentOutOfRangeException(nameof(d)),
        };
    }

    // LIKE ignores case for plain letters in SQLite, which is what a text search should do.
    protected override string NullSafeEquals(string left, string right) => $"{left} IS {right}";

    protected override string LargestTotalFirst(string measure, int tieBreakPosition) =>
        $"SUM({measure}) DESC NULLS LAST, {tieBreakPosition}";
}
