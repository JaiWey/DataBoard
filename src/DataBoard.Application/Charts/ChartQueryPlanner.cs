using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataBoard.Application.Common;
using DataBoard.Domain.Datasets;

namespace DataBoard.Application.Charts;

/// <summary>
/// Checks a client's <see cref="ChartQuery"/> against the dataset and turns it into a <see cref="ChartQueryPlan"/>.
/// This is the gate that makes the SQL renderer safe: after planning, every column is a real dataset column,
/// every alias is a plain identifier, every operator fits its column type, and every value is already typed.
/// </summary>
public static partial class ChartQueryPlanner
{
    public const int MaxDimensions = 10;
    public const int MaxMeasures = 20;
    public const int MaxFilters = 20;
    public const int MaxOrderBy = 10;
    public const int MaxListValues = 500;
    public const int MaxTextValueLength = 1_000;
    public const int MaxPatternLength = 200;
    public const int DefaultLimit = 100;
    public const int MaxLimit = 5_000;
    public const int MaxSeriesLimit = 100;
    public const int MaxAliasLength = 63; // PostgreSQL silently cuts longer identifiers, which could make two aliases collide

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex AliasPattern();

    public static ChartQueryPlan Plan(IReadOnlyList<DatasetColumn> columns, ChartQuery query)
    {
        var errors = new Errors();
        var byName = columns.ToDictionary(c => c.Name, StringComparer.Ordinal);

        var dimensionSpecs = query.Dimensions ?? [];
        var measureSpecs = query.Measures ?? [];
        var filterSpecs = query.Filters ?? [];
        var havingSpecs = query.MeasureFilters ?? [];
        var orderSpecs = query.OrderBy ?? [];

        Limit(errors, "Dimensions", dimensionSpecs.Count, MaxDimensions);
        Limit(errors, "Measures", measureSpecs.Count, MaxMeasures);
        Limit(errors, "Filters", filterSpecs.Count, MaxFilters);
        Limit(errors, "MeasureFilters", havingSpecs.Count, MaxFilters);
        Limit(errors, "OrderBy", orderSpecs.Count, MaxOrderBy);
        if (dimensionSpecs.Count + measureSpecs.Count == 0)
            errors.Add("Query", "Choose at least one dimension or measure.");
        if (errors.Any) errors.ThrowIfAny();

        var dimensions = new List<PlannedDimension>();
        for (var i = 0; i < dimensionSpecs.Count; i++)
        {
            var spec = dimensionSpecs[i];
            var field = $"Dimensions[{i}]";
            if (spec is null || !TryColumn(byName, spec.Column, field, errors, out var column)) continue;

            var kind = ColumnKinds.Of(column.DataType);
            if (spec.Grain is not null && !ColumnKinds.IsTemporal(kind))
            {
                errors.Add(field, $"A time grain can only be used on date or timestamp columns; '{column.Name}' is {column.DataType}.");
                continue;
            }

            var alias = spec.Alias ?? (spec.Grain is null ? column.Name : $"{column.Name}_{spec.Grain.ToString()!.ToLowerInvariant()}");
            if (!CheckAlias(alias, spec.Alias is not null, field, errors)) continue;
            dimensions.Add(new PlannedDimension(column.Name, column.DataType, spec.Grain, alias));
        }

        var measures = new List<PlannedMeasure>();
        for (var i = 0; i < measureSpecs.Count; i++)
        {
            var spec = measureSpecs[i];
            var field = $"Measures[{i}]";
            if (spec is null) continue;

            if (!Enum.IsDefined(spec.Aggregation))
            {
                errors.Add(field, "Unknown aggregation.");
                continue;
            }

            DatasetColumn? column = null;
            if (spec.Column is null)
            {
                if (spec.Aggregation != Aggregation.Count)
                {
                    errors.Add(field, $"{spec.Aggregation} needs a column; only Count may be used without one.");
                    continue;
                }
            }
            else if (!TryColumn(byName, spec.Column, field, errors, out column))
            {
                continue;
            }

            if (column is not null && !AggregationAllowed(spec.Aggregation, ColumnKinds.Of(column.DataType)))
            {
                errors.Add(field, $"{spec.Aggregation} cannot be applied to '{column.Name}' ({column.DataType}).");
                continue;
            }

            var alias = spec.Alias ?? DefaultMeasureAlias(spec.Aggregation, column?.Name);
            if (!CheckAlias(alias, spec.Alias is not null, field, errors)) continue;
            measures.Add(new PlannedMeasure(spec.Aggregation, column?.Name, alias));
        }

        var duplicate = dimensions.Select(d => d.Alias).Concat(measures.Select(m => m.Alias))
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            errors.Add("Query", $"The name '{duplicate.Key}' is used more than once in the result. Give each item a distinct alias.");

        var filters = new List<PlannedFilter>();
        for (var i = 0; i < filterSpecs.Count; i++)
        {
            var spec = filterSpecs[i];
            var field = $"Filters[{i}]";
            if (spec is null || !TryColumn(byName, spec.Column, field, errors, out var column)) continue;

            var kind = ColumnKinds.Of(column.DataType);
            if (TryPlanFilter(spec, kind, column, field, errors, out var values))
                filters.Add(new PlannedFilter(column.Name, spec.Operator, values));
        }

        var measureFilters = new List<PlannedMeasureFilter>();
        for (var i = 0; i < havingSpecs.Count; i++)
        {
            var spec = havingSpecs[i];
            var field = $"MeasureFilters[{i}]";
            if (spec is null) continue;

            var measure = measures.FirstOrDefault(m => string.Equals(m.Alias, spec.Column, StringComparison.OrdinalIgnoreCase));
            if (measure is null)
            {
                errors.Add(field, $"'{spec.Column}' is not one of the measures in this query.");
                continue;
            }

            if (!IsNumericResult(measure, byName))
            {
                errors.Add(field, $"'{measure.Alias}' is not a number, so it cannot be filtered this way.");
                continue;
            }

            if (spec.Operator is not (FilterOperator.Eq or FilterOperator.NotEq or FilterOperator.Gt or FilterOperator.Gte
                or FilterOperator.Lt or FilterOperator.Lte or FilterOperator.Between))
            {
                errors.Add(field, $"{spec.Operator} is not available for measure filters.");
                continue;
            }

            if (TryPlanFilter(spec, ColumnKind.Numeric, new DatasetColumn(measure.Alias, "numeric", ColumnRole.Measure), field, errors, out var values))
                measureFilters.Add(new PlannedMeasureFilter(measure, spec.Operator, values.Cast<decimal>().ToList()));
        }

        var orders = new List<PlannedOrder>();
        var seenOrder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < orderSpecs.Count; i++)
        {
            var spec = orderSpecs[i];
            var field = $"OrderBy[{i}]";
            if (spec is null) continue;

            if (!Enum.IsDefined(spec.Direction))
            {
                errors.Add(field, "Unknown sort direction.");
                continue;
            }

            var position = dimensions.FindIndex(d => string.Equals(d.Alias, spec.Field, StringComparison.OrdinalIgnoreCase));
            if (position < 0)
            {
                var m = measures.FindIndex(x => string.Equals(x.Alias, spec.Field, StringComparison.OrdinalIgnoreCase));
                if (m >= 0) position = dimensions.Count + m;
            }

            if (position < 0)
            {
                errors.Add(field, $"'{spec.Field}' is not a column of the result.");
                continue;
            }

            if (!seenOrder.Add(spec.Field))
            {
                errors.Add(field, $"'{spec.Field}' is sorted more than once.");
                continue;
            }

            orders.Add(new PlannedOrder(position + 1, spec.Direction));
        }

        // A stable, readable default: grouped results come back sorted by their dimensions.
        if (orderSpecs.Count == 0)
            orders.AddRange(Enumerable.Range(1, dimensions.Count).Select(p => new PlannedOrder(p, SortDirection.Asc)));

        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
            errors.Add("Limit", $"Limit must be between 1 and {MaxLimit}.");

        // The series is the second dimension; the series are ranked by the first measure, so it has to be a number.
        if (query.SeriesLimit is { } seriesLimit)
        {
            if (seriesLimit < 1 || seriesLimit > MaxSeriesLimit)
                errors.Add("SeriesLimit", $"The series limit must be between 1 and {MaxSeriesLimit}.");
            else if (dimensions.Count < 2)
                errors.Add("SeriesLimit", "A series limit needs a second dimension to split the chart into series.");
            else if (measures.Count == 0 || !IsNumericResult(measures[0], byName))
                errors.Add("SeriesLimit", "A series limit ranks the series by the first measure, which has to be a number.");
        }

        errors.ThrowIfAny();
        return new ChartQueryPlan(dimensions, measures, filters, measureFilters, orders, limit, query.SeriesLimit);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static void Limit(Errors errors, string name, int count, int max)
    {
        if (count > max) errors.Add(name, $"At most {max} entries are allowed.");
    }

    private static bool TryColumn(
        Dictionary<string, DatasetColumn> byName, string? name, string field, Errors errors, out DatasetColumn column)
    {
        if (name is not null && byName.TryGetValue(name, out column!)) return true;

        column = null!;
        errors.Add(field, $"'{name}' is not a column of this dataset.");
        return false;
    }

    private static bool CheckAlias(string alias, bool explicitAlias, string field, Errors errors)
    {
        if (alias.Length > MaxAliasLength)
        {
            errors.Add(field, $"The result name '{alias[..20]}...' is longer than {MaxAliasLength} characters. Give it a shorter alias.");
            return false;
        }

        if (explicitAlias && !AliasPattern().IsMatch(alias))
        {
            errors.Add(field, "An alias must start with a letter or underscore and contain only letters, digits and underscores.");
            return false;
        }

        return true;
    }

    private static string DefaultMeasureAlias(Aggregation aggregation, string? column)
    {
        var name = aggregation switch
        {
            Aggregation.CountDistinct => "count_distinct",
            _ => aggregation.ToString().ToLowerInvariant(),
        };
        return column is null ? name : $"{name}_{column}";
    }

    private static bool AggregationAllowed(Aggregation aggregation, ColumnKind kind) => aggregation switch
    {
        Aggregation.Sum or Aggregation.Avg => kind == ColumnKind.Numeric,
        Aggregation.Min or Aggregation.Max => ColumnKinds.IsOrdered(kind),
        _ => true, // Count and CountDistinct work on anything
    };

    private static bool IsNumericResult(PlannedMeasure measure, Dictionary<string, DatasetColumn> byName) =>
        measure.Aggregation switch
        {
            Aggregation.Sum or Aggregation.Avg or Aggregation.Count or Aggregation.CountDistinct => true,
            _ => measure.Column is not null && ColumnKinds.Of(byName[measure.Column].DataType) == ColumnKind.Numeric,
        };

    private static bool TryPlanFilter(
        FilterSpec spec, ColumnKind kind, DatasetColumn column, string field, Errors errors, out IReadOnlyList<object> values)
    {
        values = [];

        if (!Enum.IsDefined(spec.Operator))
        {
            errors.Add(field, "Unknown operator.");
            return false;
        }

        var allowed = spec.Operator switch
        {
            FilterOperator.Eq or FilterOperator.NotEq or FilterOperator.In or FilterOperator.NotIn => ColumnKinds.SupportsEquality(kind),
            FilterOperator.Gt or FilterOperator.Gte or FilterOperator.Lt or FilterOperator.Lte or FilterOperator.Between => ColumnKinds.IsOrdered(kind),
            FilterOperator.Contains or FilterOperator.StartsWith => kind == ColumnKind.Text,
            _ => true, // IsNull, IsNotNull
        };
        if (!allowed)
        {
            errors.Add(field, $"{spec.Operator} cannot be used on '{column.Name}' ({column.DataType}).");
            return false;
        }

        List<JsonElement> raw;
        switch (spec.Operator)
        {
            case FilterOperator.IsNull or FilterOperator.IsNotNull:
                return true;

            case FilterOperator.In or FilterOperator.NotIn:
                raw = spec.Values?.ToList() ?? [];
                if (raw.Count is 0 or > MaxListValues)
                {
                    errors.Add(field, $"{spec.Operator} needs between 1 and {MaxListValues} values.");
                    return false;
                }
                break;

            case FilterOperator.Between:
                raw = spec.Values?.ToList() ?? [];
                if (raw.Count != 2)
                {
                    errors.Add(field, "Between needs exactly two values: the lower and the upper bound.");
                    return false;
                }
                break;

            default:
                if (spec.Value is not { } single || single.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    errors.Add(field, $"{spec.Operator} needs a value.");
                    return false;
                }
                raw = [single];
                break;
        }

        var converted = new List<object>();
        foreach (var element in raw)
        {
            if (!TryConvert(kind, element, out var value, out var problem))
            {
                errors.Add(field, $"{problem} (column '{column.Name}', {column.DataType}).");
                return false;
            }
            converted.Add(value);
        }

        if (spec.Operator is FilterOperator.Contains or FilterOperator.StartsWith)
        {
            var text = (string)converted[0];
            if (text.Length is 0 or > MaxPatternLength)
            {
                errors.Add(field, $"The search text must be between 1 and {MaxPatternLength} characters.");
                return false;
            }
        }

        values = converted;
        return true;
    }

    /// <summary>Turns a JSON value into the .NET type the database driver expects for that column.</summary>
    private static bool TryConvert(ColumnKind kind, JsonElement element, out object value, out string problem)
    {
        value = null!;
        problem = string.Empty;

        switch (kind)
        {
            case ColumnKind.Numeric:
                if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
                {
                    value = number;
                    return true;
                }
                if (element.ValueKind == JsonValueKind.String
                    && decimal.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    value = number;
                    return true;
                }
                problem = "Expected a number";
                return false;

            case ColumnKind.Boolean:
                if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    value = element.GetBoolean();
                    return true;
                }
                problem = "Expected true or false";
                return false;

            case ColumnKind.Text:
                if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: <= MaxTextValueLength } text)
                {
                    value = text;
                    return true;
                }
                problem = $"Expected text of at most {MaxTextValueLength} characters";
                return false;

            case ColumnKind.Uuid:
                if (element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var guid))
                {
                    value = guid;
                    return true;
                }
                problem = "Expected a UUID";
                return false;

            case ColumnKind.Date:
                if (element.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    value = date;
                    return true;
                }
                problem = "Expected a date as yyyy-MM-dd";
                return false;

            case ColumnKind.Timestamp or ColumnKind.TimestampTz:
                if (element.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant))
                {
                    // timestamptz needs a UTC DateTime; "timestamp without time zone" needs an Unspecified one,
                    // otherwise the driver would send it as timestamptz and PostgreSQL would shift it by the session time zone.
                    value = kind == ColumnKind.TimestampTz
                        ? instant.UtcDateTime
                        : DateTime.SpecifyKind(instant.UtcDateTime, DateTimeKind.Unspecified);
                    return true;
                }
                problem = "Expected a date and time such as 2026-03-05T10:30:00Z";
                return false;

            default:
                problem = "This column type cannot be filtered";
                return false;
        }
    }

    private sealed class Errors
    {
        private readonly Dictionary<string, List<string>> _errors = new();

        public bool Any => _errors.Count > 0;

        public void Add(string field, string message)
        {
            if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
            list.Add(message);
        }

        public void ThrowIfAny()
        {
            if (_errors.Count > 0)
                throw new ValidationException(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }
}
