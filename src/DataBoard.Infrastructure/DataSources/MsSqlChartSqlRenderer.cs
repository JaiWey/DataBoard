using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Domain.DataSources;
using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// SQL Server does not accept a WITH, or an ORDER BY without TOP, inside a subquery, and the dataset's SQL is placed in a
/// subquery. A dataset written as <c>WITH a AS (...) SELECT ...</c> is therefore taken apart: its CTEs go in front of the whole
/// query, and its (meaningless inside a subquery) ORDER BY is dropped. Anything else is left exactly as it was written.
/// </summary>
internal static class MsSqlDataset
{
    /// <param name="keepOrder">
    /// Keep the ORDER BY, made legal inside a subquery with <c>OFFSET 0 ROWS</c>: a preview shows the rows in the order the
    /// author wrote. A chart groups the rows again, so for it the order is dropped.
    /// </param>
    public static (string? Ctes, string Body) Split(string sql, bool keepOrder = false)
    {
        Sequence<Statement> statements;
        try
        {
            statements = new SqlQueryParser().Parse(sql, new MsSqlDialect());
        }
        catch (Exception)
        {
            return (null, sql); // the validator has already accepted it; whatever cannot be taken apart is used as it is
        }
        if (statements.Count != 1 || statements[0] is not Statement.Select select) return (null, sql);

        var query = select.Query;
        var ctes = query.With is { CteTables.Count: > 0 } with ? string.Join(",\n", with.CteTables.Select(c => c.ToSql())) : null;
        var unusableOrder = query.OrderBy is not null && query.Limit is null && query.Offset is null && query.Fetch is null && !HasTop(query.Body);
        if (ctes is null && !unusableOrder) return (null, sql);
        if (ctes is null) return (null, keepOrder ? sql + "\nOFFSET 0 ROWS" : (query with { OrderBy = null }).ToSql());

        var rest = (query with { With = null, OrderBy = unusableOrder && !keepOrder ? null : query.OrderBy }).ToSql();
        return (ctes, unusableOrder && keepOrder ? rest + "\nOFFSET 0 ROWS" : rest);
    }

    private static bool HasTop(SetExpression body) => body switch
    {
        SetExpression.SelectExpression s => s.Select.Top is not null,
        SetExpression.QueryExpression q => q.Query.OrderBy is not null || HasTop(q.Query.Body),
        _ => false,
    };

    /// <summary>The statement that wraps a validated query so only its first rows are read, with the dataset taken apart as above.</summary>
    public static string Wrap(string validatedSql, int rows)
    {
        var (ctes, body) = Split(validatedSql, keepOrder: true);
        return (ctes is null ? string.Empty : $"WITH {ctes}\n") + $"SELECT TOP ({rows}) * FROM (\n{body}\n) AS databoard_query";
    }
}

/// <summary>
/// Writes T-SQL for a validated <see cref="ChartQueryPlan"/>. Names are in brackets, rows are limited with TOP, GROUP BY repeats
/// the expression (it has no positions), and an average or a sum of whole numbers is widened first (SQL Server would otherwise
/// average whole numbers as a whole number, and fail when a sum passes two billion).
/// </summary>
internal sealed class MsSqlChartSqlRenderer : SqlChartRendererBase
{
    public override DataSourceType Type => DataSourceType.SqlServer;

    internal static string QuoteIdentifier(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    protected override string Quote(string identifier) => QuoteIdentifier(identifier);

    protected override (string? Ctes, string Body) SplitDataset(string datasetSql) => MsSqlDataset.Split(datasetSql);

    protected override string DimensionExpression(PlannedDimension d)
    {
        var column = Quote(d.Column);
        return d.Grain switch
        {
            null => column,
            TimeGrain.Day => $"CAST({column} AS date)",
            TimeGrain.Month => $"DATEFROMPARTS(YEAR({column}), MONTH({column}), 1)",
            TimeGrain.Year => $"DATEFROMPARTS(YEAR({column}), 1, 1)",
            TimeGrain.Quarter => $"DATEFROMPARTS(YEAR({column}), (DATEPART(quarter, {column}) - 1) * 3 + 1, 1)",
            // The Monday of the week whatever the server's first day of the week is set to.
            TimeGrain.Week => $"DATEADD(day, -((DATEPART(weekday, {column}) + @@DATEFIRST - 2) % 7), CAST({column} AS date))",
            _ => throw new ArgumentOutOfRangeException(nameof(d)),
        };
    }

    protected override string GroupByItem(string expression, int position) => expression;

    protected override string TopPrefix(int rows) => $"TOP ({rows}) ";

    protected override string LimitSuffix(int rows) => string.Empty;

    protected override string NullSafeEquals(string left, string right) => $"({left} = {right} OR ({left} IS NULL AND {right} IS NULL))";

    // NULLs sort lowest in SQL Server, so a descending order already ends with them.
    protected override string LargestTotalFirst(string measure, int tieBreakPosition) => $"SUM({measure}) DESC, {tieBreakPosition}";

    protected override string LikeEscape => "ESCAPE '\\'";

    // In a LIKE pattern "[" opens a set of characters, so it has to be escaped as well.
    protected override string EscapePattern(string text) => EscapeLike(text).Replace("[", "\\[");

    protected override string MeasureExpression(PlannedMeasure m)
    {
        var column = m.Column is null ? null : Quote(m.Column);
        return m.Aggregation switch
        {
            Aggregation.Sum => $"SUM({column} * CAST(1 AS bigint))",
            Aggregation.Avg => $"AVG({column} * 1.0)",
            _ => base.MeasureExpression(m),
        };
    }
}

/// <summary>The table view SQL for SQL Server: names in brackets; every kind of join is available.</summary>
internal sealed class MsSqlTableViewSqlRenderer : SqlTableViewRendererBase
{
    public override DataSourceType Type => DataSourceType.SqlServer;

    protected override string Quote(string identifier) => MsSqlChartSqlRenderer.QuoteIdentifier(identifier);
}
