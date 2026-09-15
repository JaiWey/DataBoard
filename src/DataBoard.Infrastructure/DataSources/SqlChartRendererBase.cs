using System.Text;
using DataBoard.Application.Charts;
using DataBoard.Application.DataSources;
using DataBoard.Domain.DataSources;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// The shape of the SQL that is built for a validated <see cref="ChartQueryPlan"/>, the same for every database; what
/// differs between databases (quoting, dates, limits, null handling) is left to the subclass. It does not use a general
/// query builder on purpose: such builders parse identifiers and raw SQL for special characters (a column called "a.b"
/// or "x as y", or a "?" in SQL), and a user's dataset can contain all of those. Here the only text that is not fixed
/// is (1) identifiers from the plan, always quoted by the dialect, and (2) the dataset's own SQL, which was validated and
/// is placed untouched inside a subquery. Values are bound parameters named <c>@databoard_p0</c>, <c>@databoard_p1</c>, ...
/// </summary>
internal abstract class SqlChartRendererBase : IChartSqlRenderer
{
    protected const string DatasetAlias = "databoard_dataset";
    protected const string BaseAlias = "databoard_base";
    protected const string TopAlias = "databoard_top_series";

    public abstract DataSourceType Type { get; }

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

        // In the CTE the query is cut by nothing; the outer query takes the limit.
        sql.Append("SELECT ").Append(limitSeries ? string.Empty : TopPrefix(plan.Limit + 1)).AppendJoin(", ", select);
        // The dataset's SQL goes in verbatim, on its own lines so a trailing "-- comment" cannot swallow the parenthesis.
        sql.Append("\nFROM (\n").Append(datasetSql).Append("\n) AS ").Append(Quote(DatasetAlias));

        var where = plan.Filters.Select(f => Condition(Quote(f.Column), f.Operator, f.Values, Bind)).ToList();
        if (where.Count > 0)
            sql.Append("\nWHERE ").AppendJoin(" AND ", where);

        if (plan.Dimensions.Count > 0)
            sql.Append("\nGROUP BY ").AppendJoin(", ", GroupByItems(plan));

        var having = plan.MeasureFilters
            .Select(f => Condition(MeasureExpression(f.Measure), f.Operator, f.Values.Cast<object>().ToList(), Bind)).ToList();
        if (having.Count > 0)
            sql.Append("\nHAVING ").AppendJoin(" AND ", having);

        if (limitSeries)
        {
            // The series is the second dimension. Series are ranked by their total of the first measure; a NULL
            // series is still a series, hence the null-safe comparison. The outer SELECT lists the same columns in the
            // same order, so the ORDER BY positions below still apply.
            var series = Quote(plan.Dimensions[1].Alias);
            sql.Append("\n), ").Append(Quote(TopAlias)).Append(" AS (\n")
                .Append("SELECT ").Append(TopPrefix(plan.SeriesLimit!.Value)).Append(series).Append(" FROM ").Append(Quote(BaseAlias))
                .Append(" GROUP BY ").Append(GroupByItem(series, 1)).Append(" ORDER BY ").Append(LargestTotalFirst(Quote(plan.Measures[0].Alias), 1));
            AppendLimit(sql, plan.SeriesLimit.Value, " ");
            sql.Append("\n)\nSELECT ").Append(TopPrefix(plan.Limit + 1)).Append("b.* FROM ").Append(Quote(BaseAlias)).Append(" AS b WHERE EXISTS (SELECT 1 FROM ")
                .Append(Quote(TopAlias)).Append(" AS t WHERE ").Append(NullSafeEquals("t." + series, "b." + series)).Append(')');
        }

        if (plan.OrderBy.Count > 0)
        {
            sql.Append("\nORDER BY ").AppendJoin(", ",
                plan.OrderBy.Select(o => $"{o.Position} {(o.Direction == SortDirection.Desc ? "DESC" : "ASC")}"));
        }

        // One extra row tells the caller the result was cut.
        AppendLimit(sql, plan.Limit + 1, "\n");

        return new ParameterizedQuery(sql.ToString(), parameters);
    }

    private void AppendLimit(StringBuilder sql, int rows, string separator)
    {
        var suffix = LimitSuffix(rows);
        if (suffix.Length > 0) sql.Append(separator).Append(suffix);
    }

    // ---- what a dialect decides --------------------------------------------------------------------------------

    /// <summary>Quotes an identifier so that whatever it contains is one name.</summary>
    protected abstract string Quote(string identifier);

    /// <summary>The expression for a dimension: the column itself, or the column cut to a day, month, ...</summary>
    protected abstract string DimensionExpression(PlannedDimension d);

    /// <summary>The comparison that treats two NULLs as equal.</summary>
    protected abstract string NullSafeEquals(string left, string right);

    /// <summary>
    /// Orders series by the total of a measure, largest first, with series that have no total (NULL) last, and then by the
    /// select item at <paramref name="tieBreakPosition"/>, so which series is kept is the same every time.
    /// </summary>
    protected abstract string LargestTotalFirst(string measure, int tieBreakPosition);

    /// <summary>What GROUP BY says for one select item: its position, or (where positions are not allowed) the expression again.</summary>
    protected virtual string GroupByItem(string expression, int position) => position.ToString();

    /// <summary>What goes after <c>SELECT</c> to keep only the first <paramref name="rows"/> rows, for databases that say so up front (TOP).</summary>
    protected virtual string TopPrefix(int rows) => string.Empty;

    /// <summary>The clause at the end of the query that keeps only the first <paramref name="rows"/> rows (LIMIT), or nothing.</summary>
    protected virtual string LimitSuffix(int rows) => "LIMIT " + rows;

    /// <summary>The keyword for a case-insensitive pattern match.</summary>
    protected virtual string LikeKeyword => "LIKE";

    /// <summary>The escape character clause for patterns, as the database wants a backslash written.</summary>
    protected virtual string LikeEscape => "ESCAPE '\\'";

    private IEnumerable<string> GroupByItems(ChartQueryPlan plan) =>
        plan.Dimensions.Select((d, i) => GroupByItem(DimensionExpression(d), i + 1));

    protected virtual string MeasureExpression(PlannedMeasure m)
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

    private string Condition(string expression, FilterOperator op, IReadOnlyList<object> values, Func<object, string> bind) =>
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
            FilterOperator.Contains => $"{expression} {LikeKeyword} {bind("%" + EscapeLike((string)values[0]) + "%")} {LikeEscape}",
            FilterOperator.StartsWith => $"{expression} {LikeKeyword} {bind(EscapeLike((string)values[0]) + "%")} {LikeEscape}",
            FilterOperator.IsNull => $"{expression} IS NULL",
            FilterOperator.IsNotNull => $"{expression} IS NOT NULL",
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

    /// <summary>So that a search for "50%" finds a literal percent sign instead of acting as a wildcard.</summary>
    internal static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
