using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Domain.DataSources;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// Writes MySQL for a validated <see cref="ChartQueryPlan"/>. Identifiers are quoted with backticks; a time grain is the date
/// functions MySQL has (the result is a DATE, like PostgreSQL's <c>date_trunc</c> of a date). Text searches ignore case when the
/// column's collation does, which is MySQL's default.
/// </summary>
internal sealed class MySqlChartSqlRenderer : SqlChartRendererBase
{
    public override DataSourceType Type => DataSourceType.MySql;

    internal static string QuoteIdentifier(string identifier) => "`" + identifier.Replace("`", "``") + "`";

    protected override string Quote(string identifier) => QuoteIdentifier(identifier);

    protected override string DimensionExpression(PlannedDimension d)
    {
        var column = Quote(d.Column);
        return d.Grain switch
        {
            null => column,
            TimeGrain.Day => $"DATE({column})",
            TimeGrain.Month => $"DATE_SUB(DATE({column}), INTERVAL DAYOFMONTH({column}) - 1 DAY)",
            TimeGrain.Year => $"MAKEDATE(YEAR({column}), 1)",
            TimeGrain.Quarter => $"DATE_ADD(MAKEDATE(YEAR({column}), 1), INTERVAL QUARTER({column}) - 1 QUARTER)",
            // WEEKDAY counts Monday as 0, so this is the Monday of the week, as PostgreSQL's date_trunc gives.
            TimeGrain.Week => $"DATE_SUB(DATE({column}), INTERVAL WEEKDAY({column}) DAY)",
            _ => throw new ArgumentOutOfRangeException(nameof(d)),
        };
    }

    protected override string NullSafeEquals(string left, string right) => $"{left} <=> {right}";

    // MySQL has no NULLS LAST: "IS NULL" is 0 for a total and 1 for none, so totals come first.
    protected override string LargestTotalFirst(string measure, int tieBreakPosition) =>
        $"SUM({measure}) IS NULL, SUM({measure}) DESC, {tieBreakPosition}";

    // In a string literal MySQL reads a backslash as an escape itself, so the single backslash of the pattern is written twice.
    protected override string LikeEscape => "ESCAPE '\\\\'";
}

/// <summary>
/// The table view SQL for MySQL: identifiers in backticks, and no full outer join (MySQL does not have one), which is refused
/// in words rather than left to fail as a syntax error.
/// </summary>
internal sealed class MySqlTableViewSqlRenderer : SqlTableViewRendererBase
{
    public override DataSourceType Type => DataSourceType.MySql;

    protected override string Quote(string identifier) => MySqlChartSqlRenderer.QuoteIdentifier(identifier);

    protected override string JoinKeyword(JoinType type) =>
        type == JoinType.Full
            ? throw new ValidationException("Definition", "MySQL has no full outer join. Use a left or right join instead.")
            : base.JoinKeyword(type);
}
