using DataBoard.Application.Common;
using DataBoard.Application.DataSources;

namespace DataBoard.Application.Datasets;

/// <summary>
/// Checks a table view definition against the data source's real tables and columns, and turns it into a plan.
/// Nothing the user typed reaches the SQL unchecked: every table and column must exist in the schema.
/// </summary>
public static class TableViewPlanner
{
    public const int MaxJoins = 10;
    public const int MaxConditionsPerJoin = 5;
    public const int MaxFields = 200;

    public static TableViewPlan Plan(DatabaseSchema schema, TableViewDefinition? definition)
    {
        if (definition?.Main is null)
            throw new ValidationException("Definition", "Choose a main table.");

        var joins = definition.Joins ?? [];
        if (joins.Count > MaxJoins)
            throw new ValidationException("Definition", $"A table view can join at most {MaxJoins} tables.");

        var tables = new List<PlannedTable>();
        var columnsOfTable = new List<HashSet<string>>(); // by step, every column the table has
        var selected = new List<(int Table, string Column, string TableName)>();

        void AddTable(TableViewTable table, JoinType? join, IReadOnlyList<PlannedCondition> on, string where)
        {
            var found = Find(schema, table, where);
            tables.Add(new PlannedTable(tables.Count, found.Schema, found.Name, join, on));
            columnsOfTable.Add(found.Columns.ToHashSet(StringComparer.Ordinal));

            foreach (var field in (table.Fields ?? []).Distinct(StringComparer.Ordinal))
            {
                if (!columnsOfTable[^1].Contains(field))
                    throw new ValidationException("Definition", $"{where}: '{table.Name}' has no field '{field}'.");
                selected.Add((tables.Count - 1, field, found.Name));
            }
        }

        AddTable(definition.Main, null, [], "Main table");

        for (var i = 0; i < joins.Count; i++)
        {
            var join = joins[i];
            var step = i + 1;
            var where = $"Join {step}";
            if (join?.Table is null)
                throw new ValidationException("Definition", $"{where}: choose a table.");
            if (!Enum.IsDefined(join.Type))
                throw new ValidationException("Definition", $"{where}: the join type is not valid.");

            var on = join.On ?? [];
            if (on.Count == 0)
                throw new ValidationException("Definition", $"{where}: say how '{join.Table.Name}' relates to the earlier tables.");
            if (on.Count > MaxConditionsPerJoin)
                throw new ValidationException("Definition", $"{where}: at most {MaxConditionsPerJoin} conditions are allowed.");

            // The new table is looked up first so its columns can be checked against the right-hand fields.
            var found = Find(schema, join.Table, where);
            var right = found.Columns.ToHashSet(StringComparer.Ordinal);

            var planned = new List<PlannedCondition>();
            foreach (var condition in on)
            {
                if (condition.LeftStep < 0 || condition.LeftStep >= step)
                    throw new ValidationException("Definition", $"{where}: a condition can only use tables from earlier steps.");
                if (!columnsOfTable[condition.LeftStep].Contains(condition.LeftField ?? string.Empty))
                    throw new ValidationException("Definition", $"{where}: '{tables[condition.LeftStep].Name}' has no field '{condition.LeftField}'.");
                if (!right.Contains(condition.RightField ?? string.Empty))
                    throw new ValidationException("Definition", $"{where}: '{found.Name}' has no field '{condition.RightField}'.");
                planned.Add(new PlannedCondition(condition.LeftStep, condition.LeftField!, condition.RightField!));
            }

            AddTable(join.Table, join.Type, planned, where);
        }

        if (selected.Count == 0)
            throw new ValidationException("Definition", "Choose at least one field to show.");
        if (selected.Count > MaxFields)
            throw new ValidationException("Definition", $"A table view can show at most {MaxFields} fields.");

        return new TableViewPlan(tables, NameColumns(selected));
    }

    /// <summary>
    /// A field keeps its own name; when two tables bring the same name, the later ones become "table_field"
    /// (and get a number if even that is taken), so dataset column names stay unique.
    /// </summary>
    private static List<PlannedColumn> NameColumns(List<(int Table, string Column, string TableName)> selected)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PlannedColumn>();
        foreach (var (table, column, tableName) in selected)
        {
            var alias = column;
            if (!used.Add(alias))
            {
                alias = $"{tableName}_{column}";
                for (var n = 2; !used.Add(alias); n++)
                    alias = $"{tableName}_{column}_{n}";
            }
            result.Add(new PlannedColumn(table, column, alias));
        }
        return result;
    }

    private static (string Schema, string Name, IReadOnlyList<string> Columns) Find(DatabaseSchema schema, TableViewTable table, string where)
    {
        var found = schema.Schemas
            .Where(s => s.Name == table.Schema)
            .SelectMany(s => s.Tables)
            .FirstOrDefault(t => t.Name == table.Name);

        if (found is null)
            throw new ValidationException("Definition", $"{where}: the table '{table.Schema}.{table.Name}' was not found in the data source.");

        return (table.Schema, found.Name, found.Columns.Select(c => c.Name).ToList());
    }
}
