using System.Text.Json;
using System.Text.Json.Serialization;
using DataBoard.Application.DataSources;

namespace DataBoard.Application.Datasets;

public enum JoinType { Inner, Left, Right, Full }

/// <summary>A table (or view) of the data source and the fields of it that appear in the dataset.</summary>
public sealed record TableViewTable(string Schema, string Name, IReadOnlyList<string>? Fields = null);

/// <summary>
/// One equality in a join: a field of an earlier step's table equals a field of the table being joined.
/// Step 0 is the main table, step k is the k-th join, so a join may only refer to the steps before it.
/// </summary>
public sealed record JoinCondition(int LeftStep, string LeftField, string RightField);

public sealed record TableViewJoin(JoinType Type, TableViewTable Table, IReadOnlyList<JoinCondition>? On = null);

/// <summary>What the user chose in the guided table view: a main table, then any number of joined tables.</summary>
public sealed record TableViewDefinition(TableViewTable Main, IReadOnlyList<TableViewJoin>? Joins = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static TableViewDefinition? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<TableViewDefinition>(json, Options);

    /// <summary>The definition as it was after the first <paramref name="joins"/> joins ("run this step").</summary>
    public TableViewDefinition UpToStep(int joins) => this with { Joins = (Joins ?? []).Take(Math.Max(0, joins)).ToList() };
}

// ---- the validated plan the SQL renderer works from ---------------------------------------------------------

/// <param name="Index">The table's alias number in the SQL (t0, t1, ...). The same table may appear twice.</param>
public sealed record PlannedTable(int Index, string Schema, string Name, JoinType? Join, IReadOnlyList<PlannedCondition> On);

public sealed record PlannedCondition(int LeftTable, string LeftColumn, string RightColumn);

/// <param name="Alias">The column's name in the dataset (unique, case-insensitively).</param>
public sealed record PlannedColumn(int Table, string Column, string Alias);

public sealed record TableViewPlan(IReadOnlyList<PlannedTable> Tables, IReadOnlyList<PlannedColumn> Columns);

public interface ITableViewSqlRenderer
{
    Domain.DataSources.DataSourceType Type { get; }

    /// <summary>Writes the SELECT for a validated plan. Identifiers come only from the plan and are always quoted.</summary>
    string Render(TableViewPlan plan);
}

public sealed class TableViewSqlRenderers(IEnumerable<ITableViewSqlRenderer> renderers)
{
    private readonly Dictionary<Domain.DataSources.DataSourceType, ITableViewSqlRenderer> _byType = renderers.ToDictionary(r => r.Type);

    public ITableViewSqlRenderer Get(Domain.DataSources.DataSourceType type) =>
        _byType.TryGetValue(type, out var renderer)
            ? renderer
            : throw new Common.ValidationException("Type", $"Table views on {type} data sources are not supported yet.");
}

/// <summary>A table view's result, with the SQL it was built from (shown to Editors and Owners only).</summary>
public sealed record TableViewPreviewResult(IReadOnlyList<PreviewColumn> Columns, IReadOnlyList<object?[]> Rows, bool Truncated, string Sql);
