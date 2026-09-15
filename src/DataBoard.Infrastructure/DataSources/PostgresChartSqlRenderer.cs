using DataBoard.Application.Charts;
using DataBoard.Domain.DataSources;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>Writes PostgreSQL for a validated <see cref="ChartQueryPlan"/>; see <see cref="SqlChartRendererBase"/> for the shape of it.</summary>
internal sealed class PostgresChartSqlRenderer : SqlChartRendererBase
{
    public override DataSourceType Type => DataSourceType.PostgreSql;

    /// <summary>PostgreSQL identifier quoting: wrap in double quotes and double any embedded double quote.</summary>
    internal static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    protected override string Quote(string identifier) => QuoteIdentifier(identifier);

    protected override string DimensionExpression(PlannedDimension d)
    {
        var column = Quote(d.Column);
        if (d.Grain is null) return column;

        // date_trunc on a plain date would go through timestamptz and the session time zone; a timestamp does not.
        // The connection pins the session to UTC, so timestamptz buckets are UTC as well.
        if (ColumnKinds.Of(d.ColumnType) == ColumnKind.Date) column += "::timestamp";

        return $"date_trunc('{Grain(d.Grain.Value)}', {column})";
    }

    private static string Grain(TimeGrain grain) => grain switch
    {
        TimeGrain.Day => "day",
        TimeGrain.Week => "week",
        TimeGrain.Month => "month",
        TimeGrain.Quarter => "quarter",
        TimeGrain.Year => "year",
        _ => throw new ArgumentOutOfRangeException(nameof(grain)),
    };

    protected override string LikeKeyword => "ILIKE";

    protected override string NullSafeEquals(string left, string right) => $"{left} IS NOT DISTINCT FROM {right}";

    protected override string LargestTotalFirst(string measure, int tieBreakPosition) =>
        $"SUM({measure}) DESC NULLS LAST, {tieBreakPosition}";
}
