using DataBoard.Application.Datasets;
using DataBoard.Infrastructure.DataSources;

namespace DataBoard.Infrastructure.Tests;

public class PostgresTableViewSqlRendererTests
{
    private readonly PostgresTableViewSqlRenderer _renderer = new();

    [Fact]
    public void Writes_a_select_from_the_main_table_with_quoted_names()
    {
        var plan = new TableViewPlan(
            [new PlannedTable(0, "public", "orders", null, [])],
            [new PlannedColumn(0, "id", "id"), new PlannedColumn(0, "amount", "total")]);

        var sql = _renderer.Render(plan);

        Assert.Equal(
            "SELECT t0.\"id\" AS \"id\",\n       t0.\"amount\" AS \"total\"\nFROM \"public\".\"orders\" AS t0",
            sql);
    }

    [Theory]
    [InlineData(JoinType.Inner, "INNER JOIN")]
    [InlineData(JoinType.Left, "LEFT JOIN")]
    [InlineData(JoinType.Right, "RIGHT JOIN")]
    [InlineData(JoinType.Full, "FULL JOIN")]
    public void Joins_with_every_condition(JoinType type, string keyword)
    {
        var plan = new TableViewPlan(
            [
                new PlannedTable(0, "public", "orders", null, []),
                new PlannedTable(1, "public", "customers", type, [new PlannedCondition(0, "customer_id", "id"), new PlannedCondition(0, "region", "region")]),
            ],
            [new PlannedColumn(0, "id", "id"), new PlannedColumn(1, "name", "name")]);

        var sql = _renderer.Render(plan);

        Assert.Contains($"\n{keyword} \"public\".\"customers\" AS t1\n  ON t0.\"customer_id\" = t1.\"id\"\n AND t0.\"region\" = t1.\"region\"", sql);
    }

    [Fact]
    public void Quotes_hostile_names_so_they_stay_one_identifier()
    {
        var plan = new TableViewPlan(
            [new PlannedTable(0, "pub\"lic", "or\"ders; DROP TABLE x; --", null, [])],
            [new PlannedColumn(0, "a\"b", "x\" , 1 AS \"y")]);

        var sql = _renderer.Render(plan);

        Assert.Contains("\"pub\"\"lic\".\"or\"\"ders; DROP TABLE x; --\"", sql);
        Assert.Contains("t0.\"a\"\"b\" AS \"x\"\" , 1 AS \"\"y\"", sql);
    }

    [Fact]
    public void The_same_table_can_be_joined_to_itself_under_different_aliases()
    {
        var plan = new TableViewPlan(
            [
                new PlannedTable(0, "public", "customers", null, []),
                new PlannedTable(1, "public", "customers", JoinType.Inner, [new PlannedCondition(0, "id", "id")]),
            ],
            [new PlannedColumn(0, "id", "id"), new PlannedColumn(1, "id", "customers_id")]);

        var sql = _renderer.Render(plan);

        Assert.Contains("FROM \"public\".\"customers\" AS t0", sql);
        Assert.Contains("INNER JOIN \"public\".\"customers\" AS t1", sql);
    }
}
