using System.Text.Json;
using DataBoard.Application.DataSources;

namespace DataBoard.Application.Charts;

public enum Aggregation { Sum, Avg, Min, Max, Count, CountDistinct }

public enum TimeGrain { Day, Week, Month, Quarter, Year }

public enum SortDirection { Asc, Desc }

public enum FilterOperator
{
    Eq, NotEq, Gt, Gte, Lt, Lte,
    In, NotIn, Between,
    Contains, StartsWith,
    IsNull, IsNotNull,
}

// ---- what the client sends ---------------------------------------------------------------------------------

/// <summary>A column to group by. <paramref name="Grain"/> buckets dates and timestamps (day, month, ...).</summary>
public sealed record DimensionSpec(string Column, TimeGrain? Grain = null, string? Alias = null);

/// <summary>An aggregate. <paramref name="Column"/> may be omitted only for Count, which then counts rows.</summary>
public sealed record MeasureSpec(Aggregation Aggregation, string? Column = null, string? Alias = null);

/// <summary>
/// A condition. For filters, <paramref name="Column"/> is a dataset column; for measure filters it is a measure alias.
/// Single-value operators use <paramref name="Value"/>; In/NotIn/Between use <paramref name="Values"/>.
/// </summary>
public sealed record FilterSpec(string Column, FilterOperator Operator, JsonElement? Value = null, IReadOnlyList<JsonElement>? Values = null);

public sealed record OrderSpec(string Field, SortDirection Direction = SortDirection.Asc);

public sealed record ChartQuery(
    IReadOnlyList<DimensionSpec>? Dimensions = null,
    IReadOnlyList<MeasureSpec>? Measures = null,
    IReadOnlyList<FilterSpec>? Filters = null,
    IReadOnlyList<FilterSpec>? MeasureFilters = null,
    IReadOnlyList<OrderSpec>? OrderBy = null,
    int? Limit = null);

// ---- the validated, typed form the SQL renderer works from ---------------------------------------------------

public sealed record PlannedDimension(string Column, string ColumnType, TimeGrain? Grain, string Alias);

public sealed record PlannedMeasure(Aggregation Aggregation, string? Column, string Alias);

/// <param name="Values">Already converted to the column's .NET type (decimal, bool, Guid, DateOnly, DateTime, string).</param>
public sealed record PlannedFilter(string Column, FilterOperator Operator, IReadOnlyList<object> Values);

public sealed record PlannedMeasureFilter(PlannedMeasure Measure, FilterOperator Operator, IReadOnlyList<decimal> Values);

/// <param name="Position">1-based position in the select list (dimensions first, then measures).</param>
public sealed record PlannedOrder(int Position, SortDirection Direction);

public sealed record ChartQueryPlan(
    IReadOnlyList<PlannedDimension> Dimensions,
    IReadOnlyList<PlannedMeasure> Measures,
    IReadOnlyList<PlannedFilter> Filters,
    IReadOnlyList<PlannedMeasureFilter> MeasureFilters,
    IReadOnlyList<PlannedOrder> OrderBy,
    int Limit);

// ---- rendering and results -----------------------------------------------------------------------------------

public interface IChartSqlRenderer
{
    Domain.DataSources.DataSourceType Type { get; }

    /// <summary>
    /// Builds one SELECT over the dataset's SQL. Identifiers come only from the validated plan, and every value
    /// travels as a bound parameter, never as text inside the SQL.
    /// </summary>
    ParameterizedQuery Render(string datasetSql, ChartQueryPlan plan);
}

/// <summary>Finds the renderer for a data source type.</summary>
public sealed class ChartSqlRenderers(IEnumerable<IChartSqlRenderer> renderers)
{
    private readonly Dictionary<Domain.DataSources.DataSourceType, IChartSqlRenderer> _byType = renderers.ToDictionary(r => r.Type);

    public IChartSqlRenderer Get(Domain.DataSources.DataSourceType type) =>
        _byType.TryGetValue(type, out var renderer)
            ? renderer
            : throw new Common.ValidationException("Type", $"Charts on {type} data sources are not supported yet.");
}

/// <param name="Sql">Only Editors and Owners get this: it contains the dataset's SQL, which Viewers must not see.</param>
public sealed record ChartResult(IReadOnlyList<PreviewColumn> Columns, IReadOnlyList<object?[]> Rows, bool Truncated, string? Sql);
