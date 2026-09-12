using System.Text;
using DataBoard.Application.Charts;
using DataBoard.Application.DataSources;
using DataBoard.Domain.DataSources;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// Writes PostgreSQL for a validated <see cref="ChartQueryPlan"/>. It does not use a general query builder on
/// purpose: such builders parse identifiers and raw SQL for special characters (a column called "a.b" or
/// "x as y", or a "?" in SQL), and a user's dataset can contain all of those. Here the only text that is not
/// fixed is (1) identifiers from the plan, always quoted with embedded quotes doubled, and (2) the dataset's own
/// SQL, which was validated and is placed untouched inside a subquery. Values are bound parameters.
/// </summary>
internal sealed class PostgresChartSqlRenderer : IChartSqlRenderer
{
    private const string DatasetAlias = "databoard_dataset";
    private const string BaseAlias = "databoard_base";
    private const string TopAlias = "databoard_top_series";

    public DataSourceType Type => DataSourceType.PostgreSql;

    public ParameterizedQuery Render(string datasetSql, ChartQueryPlan plan, IReadOnlyList<object?>? datasetParameters = null)
    {
        // The dataset's own parameters are already named in its SQL (databoard_p0 ...); filter values continue the numbering.
        var parameters = new List<object?>(datasetParameters ?? []);

        string Bind(object value)
        {
            parameters.Add(value);
            return "@" + ParameterizedQuery.Name(parameters.Count - 1);
        }

        var select = plan.Dimensions.Select(d => $"{DimensionExpression(d)} AS {Quote(d.Alias)}")
            .Concat(plan.Measures.Select(m => $"{MeasureExpression(m)} AS {Quote(m.Alias)}"));

        var sql = new StringBuilder();
        // With a series limit the grouped query becomes a CTE, so the top series can be picked from its rows.
        var limitSeries = plan.SeriesLimit is not null && plan.Dimensions.Count >= 2 && plan.Measures.Count > 0;
        if (limitSeries) sql.Append("WITH ").Append(Quote(BaseAlias)).Append(" AS (\n");

        sql.Append("SELECT ").AppendJoin(", ", select);
        // The dataset's SQL goes in verbatim, on its own lines so a trailing "-- comment" cannot swallow the parenthesis.
        sql.Append("\nFROM (\n").Append(datasetSql).Append("\n) AS ").Append(Quote(DatasetAlias));

        var where = plan.Filters.Select(f => Condition(Quote(f.Column), f.Operator, f.Values, Bind)).ToList();
        if (where.Count > 0)
            sql.Append("\nWHERE ").AppendJoin(" AND ", where);

        if (plan.Dimensions.Count > 0)
            sql.Append("\nGROUP BY ").AppendJoin(", ", Enumerable.Range(1, plan.Dimensions.Count));

        var having = plan.MeasureFilters
            .Select(f => Condition(MeasureExpression(f.Measure), f.Operator, f.Values.Cast<object>().ToList(), Bind)).ToList();
        if (having.Count > 0)
            sql.Append("\nHAVING ").AppendJoin(" AND ", having);

        if (limitSeries)
        {
            // The series is the second dimension. Series are ranked by their total of the first measure; a NULL
            // series is still a series, hence IS NOT DISTINCT FROM. The outer SELECT lists the same columns in the
            // same order, so the ORDER BY positions below still apply.
            var series = Quote(plan.Dimensions[1].Alias);
            sql.Append("\n), ").Append(Quote(TopAlias)).Append(" AS (\n")
                .Append("SELECT ").Append(series).Append(" FROM ").Append(Quote(BaseAlias))
                .Append(" GROUP BY 1 ORDER BY SUM(").Append(Quote(plan.Measures[0].Alias)).Append(") DESC NULLS LAST, 1")
                .Append(" LIMIT ").Append(plan.SeriesLimit)
                .Append("\n)\nSELECT b.* FROM ").Append(Quote(BaseAlias)).Append(" AS b WHERE EXISTS (SELECT 1 FROM ")
                .Append(Quote(TopAlias)).Append(" AS t WHERE t.").Append(series).Append(" IS NOT DISTINCT FROM b.").Append(series).Append(')');
        }

        if (plan.OrderBy.Count > 0)
        {
            sql.Append("\nORDER BY ").AppendJoin(", ",
                plan.OrderBy.Select(o => $"{o.Position} {(o.Direction == SortDirection.Desc ? "DESC" : "ASC")}"));
        }

        // One extra row tells the caller the result was cut.
        sql.Append("\nLIMIT ").Append(plan.Limit + 1);

        return new ParameterizedQuery(sql.ToString(), parameters);
    }

    /// <summary>PostgreSQL identifier quoting: wrap in double quotes and double any embedded double quote.</summary>
    internal static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string DimensionExpression(PlannedDimension d)
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

    private static string MeasureExpression(PlannedMeasure m)
    {
        var column = m.Column is null ? null : Quote(m.Column);
        return m.Aggregation switch
        {
            Aggregation.Sum => $"SUM({column})",
            Aggregation.Avg => $"AVG({column})",
            Aggregation.Min => $"MIN({column})",
            Aggregation.Max => $"MAX({column})",
            Aggregation.Count => column is null ? "COUNT(*)" : $"COUNT({column})",
            Aggregation.CountDistinct => $"COUNT(DISTINCT {column})",
            _ => throw new ArgumentOutOfRangeException(nameof(m)),
        };
    }

    private static string Condition(string expression, FilterOperator op, IReadOnlyList<object> values, Func<object, string> bind) =>
        op switch
        {
            FilterOperator.Eq => $"{expression} = {bind(values[0])}",
            FilterOperator.NotEq => $"{expression} <> {bind(values[0])}",
            FilterOperator.Gt => $"{expression} > {bind(values[0])}",
            FilterOperator.Gte => $"{expression} >= {bind(values[0])}",
            FilterOperator.Lt => $"{expression} < {bind(values[0])}",
            FilterOperator.Lte => $"{expression} <= {bind(values[0])}",
            FilterOperator.In => $"{expression} IN ({string.Join(", ", values.Select(bind))})",
            FilterOperator.NotIn => $"{expression} NOT IN ({string.Join(", ", values.Select(bind))})",
            FilterOperator.Between => $"{expression} BETWEEN {bind(values[0])} AND {bind(values[1])}",
            FilterOperator.Contains => $"{expression} ILIKE {bind("%" + EscapeLike((string)values[0]) + "%")} ESCAPE '\\'",
            FilterOperator.StartsWith => $"{expression} ILIKE {bind(EscapeLike((string)values[0]) + "%")} ESCAPE '\\'",
            FilterOperator.IsNull => $"{expression} IS NULL",
            FilterOperator.IsNotNull => $"{expression} IS NOT NULL",
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

    /// <summary>So that a search for "50%" finds a literal percent sign instead of acting as a wildcard.</summary>
    internal static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
