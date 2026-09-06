using System.Text;
using DataBoard.Application.Datasets;
using DataBoard.Domain.DataSources;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// Writes the PostgreSQL for a validated <see cref="TableViewPlan"/>. The only text that is not fixed is identifiers
/// (schemas, tables, columns, aliases) taken from the plan, which the planner checked against the real schema, and
/// every one is quoted with embedded quotes doubled. No value is ever written into the SQL.
/// </summary>
internal sealed class PostgresTableViewSqlRenderer : ITableViewSqlRenderer
{
    public DataSourceType Type => DataSourceType.PostgreSql;

    public string Render(TableViewPlan plan)
    {
        var sql = new StringBuilder("SELECT ");
        sql.AppendJoin(",\n       ", plan.Columns.Select(c => $"{Alias(c.Table)}.{Quote(c.Column)} AS {Quote(c.Alias)}"));

        var main = plan.Tables[0];
        sql.Append("\nFROM ").Append(TableName(main)).Append(" AS ").Append(Alias(main.Index));

        foreach (var table in plan.Tables.Skip(1))
        {
            sql.Append('\n').Append(JoinKeyword(table.Join!.Value)).Append(' ').Append(TableName(table)).Append(" AS ").Append(Alias(table.Index));
            sql.Append("\n  ON ").AppendJoin("\n AND ", table.On.Select(c =>
                $"{Alias(c.LeftTable)}.{Quote(c.LeftColumn)} = {Alias(table.Index)}.{Quote(c.RightColumn)}"));
        }

        return sql.ToString();
    }

    private static string JoinKeyword(JoinType type) => type switch
    {
        JoinType.Inner => "INNER JOIN",
        JoinType.Left => "LEFT JOIN",
        JoinType.Right => "RIGHT JOIN",
        JoinType.Full => "FULL JOIN",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static string TableName(PlannedTable table) => $"{Quote(table.Schema)}.{Quote(table.Name)}";

    /// <summary>Tables get fixed short aliases, so the same table can be joined to itself.</summary>
    private static string Alias(int index) => "t" + index;

    internal static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
