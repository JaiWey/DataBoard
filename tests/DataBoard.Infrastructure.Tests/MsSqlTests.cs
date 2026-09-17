using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Domain.DataSources;
using DataBoard.Infrastructure.DataSources;

namespace DataBoard.Infrastructure.Tests;

public class MsSqlQueryValidatorTests
{
    private readonly SqlParserQueryValidator _validator = new();

    private string Validate(string sql) => _validator.ValidateReadOnly(DataSourceType.SqlServer, sql);

    private ValidationException Rejected(string sql) => Assert.Throws<ValidationException>(() => Validate(sql));

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT [order id], SUM(amount) FROM [dbo].[sales 2026] GROUP BY [order id] HAVING SUM(amount) > 100")]
    [InlineData("SELECT TOP (5) * FROM sales ORDER BY sold_at DESC")]
    [InlineData("SELECT * FROM sales ORDER BY sold_at OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY")]
    [InlineData("WITH a AS (SELECT 1 AS x) SELECT * FROM a")]
    [InlineData("SELECT region FROM sales UNION SELECT region FROM refunds")]
    [InlineData("SELECT YEAR(sold_at), COUNT(*) FROM sales GROUP BY YEAR(sold_at)")]
    [InlineData("SELECT ISNULL(note, ''), CONCAT(a, b), CAST(x AS decimal(10, 2)) FROM sales")]
    [InlineData("SELECT * FROM sales WHERE region = @databoard_p0 AND amount >= @databoard_p1")]
    [InlineData("SELECT * FROM sales WHERE note LIKE '%50\\%%' ESCAPE '\\'")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.TABLES")]
    [InlineData("SELECT * FROM dbo.sales s CROSS APPLY (SELECT TOP 1 * FROM dbo.refunds r WHERE r.id = s.id) x")]
    public void Accepts_read_only_queries(string sql) => Assert.NotEmpty(Validate(sql));

    [Theory]
    [InlineData("DELETE FROM sales")]
    [InlineData("DROP TABLE sales")]
    [InlineData("UPDATE sales SET amount = 0")]
    [InlineData("INSERT INTO sales VALUES (1)")]
    [InlineData("TRUNCATE TABLE sales")]
    [InlineData("CREATE TABLE x (a int)")]
    [InlineData("EXEC xp_cmdshell 'dir'")]
    [InlineData("EXECUTE sp_who")]
    [InlineData("SELECT * INTO copy FROM sales")]
    [InlineData("SELECT * INTO #copy FROM sales")]
    [InlineData("WAITFOR DELAY '00:00:30'")]
    [InlineData("SELECT * FROM OPENDATASOURCE('SQLNCLI', 'Data Source=x').db.dbo.t")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1; DROP TABLE sales")]
    [InlineData("DECLARE @x int = 1; SELECT @x")]
    public void Rejects_everything_that_is_not_one_select(string sql) => Rejected(sql);

    [Theory]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=x;', 'SELECT 1')", "OPENROWSET")]
    [InlineData("SELECT * FROM OPENQUERY(linked, 'SELECT 1')", "OPENQUERY")]
    [InlineData("SELECT xp_cmdshell('dir')", "xp_cmdshell")]
    [InlineData("SELECT dbo.xp_anything(1)", "xp_anything")]
    [InlineData("SELECT * FROM fn_get_audit_file('c:\\x', DEFAULT, DEFAULT)", "fn_get_audit_file")]
    [InlineData("SELECT * FROM sys.sql_logins", "sys")]
    [InlineData("SELECT * FROM [sys].[server_principals]", "sys")]
    [InlineData("SELECT * FROM master.dbo.sysdatabases", "master")]
    [InlineData("SELECT * FROM msdb.dbo.sysjobs", "msdb")]
    public void Rejects_what_reaches_outside_the_data(string sql, string name) =>
        Assert.Contains(name, Rejected(sql).Errors["Sql"][0], StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("SELECT * FROM (SELECT xp_cmdshell('dir') AS x) AS t")]
    [InlineData("WITH x AS (SELECT * FROM sys.sql_logins) SELECT * FROM x")]
    [InlineData("SELECT * FROM sales WHERE region IN (SELECT name FROM sys.databases)")]
    [InlineData("SELECT (SELECT TOP 1 name FROM master.dbo.sysdatabases) AS leaked")]
    [InlineData("SELECT region FROM sales UNION ALL SELECT name FROM sys.sql_logins")]
    [InlineData("SELECT * FROM sales s JOIN sys.sql_logins l ON 1 = 1")]
    [InlineData("SELECT * FROM sales s CROSS APPLY (SELECT * FROM OPENROWSET('a', 'b', 'c')) x")]
    public void Looks_for_the_dangerous_part_inside_other_queries_too(string sql) => Rejected(sql);
}

public class MsSqlDatasetTests
{
    [Fact]
    public void Leaves_a_plain_query_exactly_as_written()
    {
        const string sql = "SELECT region, SUM(amount) AS total FROM sales GROUP BY region";

        Assert.Equal((null, sql), MsSqlDataset.Split(sql));
    }

    [Fact]
    public void Leaves_a_query_with_top_and_order_by_alone_because_that_is_allowed_in_a_subquery()
    {
        const string sql = "SELECT TOP (3) region FROM sales ORDER BY amount DESC";

        Assert.Equal((null, sql), MsSqlDataset.Split(sql));
    }

    [Fact]
    public void Drops_an_order_by_that_means_nothing_inside_a_subquery()
    {
        var (ctes, body) = MsSqlDataset.Split("SELECT region FROM sales ORDER BY region");

        Assert.Null(ctes);
        Assert.DoesNotContain("ORDER BY", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM sales", body);
    }

    [Fact]
    public void Keeps_the_order_for_a_preview_by_making_it_legal_with_offset_zero()
    {
        var plain = MsSqlDataset.Wrap("SELECT region FROM sales ORDER BY region", 11);
        Assert.Equal("SELECT TOP (11) * FROM (\nSELECT region FROM sales ORDER BY region\nOFFSET 0 ROWS\n) AS databoard_query", plain);

        var withCte = MsSqlDataset.Wrap("WITH a AS (SELECT 1 AS x) SELECT x FROM a ORDER BY x", 11);
        Assert.StartsWith("WITH a AS (", withCte);
        Assert.Contains("ORDER BY", withCte);
        Assert.Contains("OFFSET 0 ROWS\n) AS databoard_query", withCte);

        // TOP and a real OFFSET already make an ORDER BY legal, so nothing is added.
        Assert.DoesNotContain("OFFSET 0", MsSqlDataset.Wrap("SELECT TOP (3) region FROM sales ORDER BY region", 11));
        Assert.DoesNotContain("OFFSET 0", MsSqlDataset.Wrap("SELECT region FROM sales ORDER BY region OFFSET 5 ROWS", 11));
    }

    [Fact]
    public void Moves_the_ctes_to_the_front()
    {
        var (ctes, body) = MsSqlDataset.Split("WITH a AS (SELECT 1 AS x), b AS (SELECT x + 1 AS y FROM a) SELECT y FROM b");

        Assert.NotNull(ctes);
        Assert.Contains("a AS (", ctes);
        Assert.Contains("b AS (", ctes);
        Assert.DoesNotContain("WITH", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM b", body);
    }

    [Fact]
    public void Wraps_for_a_preview_with_top_and_the_ctes_in_front()
    {
        var sql = MsSqlDataset.Wrap("WITH a AS (SELECT 1 AS x) SELECT x FROM a", 11);

        Assert.StartsWith("WITH a AS (", sql);
        Assert.Contains("SELECT TOP (11) * FROM (\nSELECT x FROM a\n) AS databoard_query", sql);
    }

    [Fact]
    public void Wraps_a_plain_query_without_changing_it()
    {
        Assert.Equal("SELECT TOP (0) * FROM (\nSELECT 1 AS x\n) AS databoard_query", MsSqlDataset.Wrap("SELECT 1 AS x", 0));
    }
}

public class MsSqlChartSqlRendererTests
{
    private readonly MsSqlChartSqlRenderer _renderer = new();
    private const string Dataset = "SELECT * FROM sales";

    private static ChartQueryPlan Plan(
        PlannedDimension[]? dims = null, PlannedMeasure[]? measures = null, PlannedFilter[]? filters = null,
        PlannedMeasureFilter[]? having = null, PlannedOrder[]? order = null, int limit = 100, int? seriesLimit = null) =>
        new(dims ?? [], measures ?? [], filters ?? [], having ?? [], order ?? [], limit, seriesLimit);

    private static PlannedDimension Dim(string column, string type = "text", TimeGrain? grain = null, string? alias = null) =>
        new(column, type, grain, alias ?? column);

    [Fact]
    public void Renders_a_full_grouped_query_with_brackets_top_and_no_group_by_position()
    {
        var revenue = new PlannedMeasure(Aggregation.Sum, "price", "revenue");
        var plan = Plan(
            dims: [Dim("region")],
            measures: [revenue],
            filters: [new("region", FilterOperator.In, ["Auckland", "Wellington"]), new("quantity", FilterOperator.Gte, [3m])],
            having: [new(revenue, FilterOperator.Gt, [1000m])],
            order: [new(2, SortDirection.Desc)],
            limit: 10);

        var query = _renderer.Render(Dataset, plan);

        Assert.Equal(
            "SELECT TOP (11) [region] AS [region], SUM([price] * CAST(1 AS bigint)) AS [revenue]\n" +
            "FROM (\nSELECT * FROM sales\n) AS [databoard_dataset]\n" +
            "WHERE [region] IN (@databoard_p0, @databoard_p1) AND [quantity] >= @databoard_p2\n" +
            "GROUP BY [region]\n" +
            "HAVING SUM([price] * CAST(1 AS bigint)) > @databoard_p3\n" +
            "ORDER BY 2 DESC",
            query.Sql);
        Assert.Equal(new object[] { "Auckland", "Wellington", 3m, 1000m }, query.Parameters);
    }

    [Fact]
    public void A_series_limit_uses_top_in_both_places_and_the_null_safe_comparison_spelled_out()
    {
        var plan = Plan(
            dims: [Dim("at", "date", TimeGrain.Year, "year"), Dim("region")],
            measures: [new PlannedMeasure(Aggregation.Sum, "price", "revenue")],
            order: [new(1, SortDirection.Asc)],
            limit: 10,
            seriesLimit: 5);

        var query = _renderer.Render(Dataset, plan);

        Assert.Equal(
            "WITH [databoard_base] AS (\n" +
            "SELECT DATEFROMPARTS(YEAR([at]), 1, 1) AS [year], [region] AS [region], SUM([price] * CAST(1 AS bigint)) AS [revenue]\n" +
            "FROM (\nSELECT * FROM sales\n) AS [databoard_dataset]\n" +
            "GROUP BY DATEFROMPARTS(YEAR([at]), 1, 1), [region]\n" +
            "), [databoard_top_series] AS (\n" +
            "SELECT TOP (5) [region] FROM [databoard_base] GROUP BY [region] ORDER BY SUM([revenue]) DESC, 1\n" +
            ")\n" +
            "SELECT TOP (11) b.* FROM [databoard_base] AS b WHERE EXISTS (SELECT 1 FROM [databoard_top_series] AS t WHERE (t.[region] = b.[region] OR (t.[region] IS NULL AND b.[region] IS NULL)))\n" +
            "ORDER BY 1 ASC",
            query.Sql);
    }

    [Fact]
    public void The_datasets_own_ctes_go_in_front_of_the_query_and_of_the_series_ctes()
    {
        var plain = _renderer.Render("WITH a AS (SELECT 1 AS x) SELECT x FROM a", Plan(measures: [new PlannedMeasure(Aggregation.Count, null, "n")], limit: 5));
        Assert.StartsWith("WITH a AS (", plain.Sql);
        Assert.Contains("SELECT TOP (6) COUNT(*) AS [n]\nFROM (\nSELECT x FROM a\n) AS [databoard_dataset]", plain.Sql);

        var series = _renderer.Render("WITH a AS (SELECT 1 AS x) SELECT x FROM a", Plan(
            dims: [Dim("x"), Dim("y")], measures: [new PlannedMeasure(Aggregation.Count, null, "n")], seriesLimit: 2));
        Assert.StartsWith("WITH a AS (", series.Sql);
        Assert.Contains("),\n[databoard_base] AS (", series.Sql); // one WITH, a list
        Assert.Equal(1, series.Sql.Split("WITH ").Length - 1);
    }

    [Theory]
    [InlineData(TimeGrain.Day, "CAST([at] AS date)")]
    [InlineData(TimeGrain.Month, "DATEFROMPARTS(YEAR([at]), MONTH([at]), 1)")]
    [InlineData(TimeGrain.Quarter, "DATEFROMPARTS(YEAR([at]), (DATEPART(quarter, [at]) - 1) * 3 + 1, 1)")]
    [InlineData(TimeGrain.Year, "DATEFROMPARTS(YEAR([at]), 1, 1)")]
    [InlineData(TimeGrain.Week, "DATEADD(day, -((DATEPART(weekday, [at]) + @@DATEFIRST - 2) % 7), CAST([at] AS date))")]
    public void Cuts_a_time_to_a_grain_and_groups_by_the_same_expression(TimeGrain grain, string expected)
    {
        var query = _renderer.Render(Dataset, Plan(dims: [Dim("at", "date", grain, "d")], limit: 5));

        Assert.Contains($"SELECT TOP (6) {expected} AS [d]", query.Sql);
        Assert.Contains($"GROUP BY {expected}", query.Sql);
    }

    [Fact]
    public void Widens_whole_numbers_so_an_average_has_decimals_and_a_sum_does_not_overflow()
    {
        var plan = Plan(measures:
        [
            new PlannedMeasure(Aggregation.Avg, "quantity", "mean"),
            new PlannedMeasure(Aggregation.Sum, "quantity", "total"),
            new PlannedMeasure(Aggregation.Max, "quantity", "top"),
            new PlannedMeasure(Aggregation.CountDistinct, "region", "regions"),
        ], limit: 1);

        var sql = _renderer.Render(Dataset, plan).Sql;

        Assert.Contains("AVG([quantity] * 1.0) AS [mean]", sql);
        Assert.Contains("SUM([quantity] * CAST(1 AS bigint)) AS [total]", sql);
        Assert.Contains("MAX([quantity]) AS [top]", sql);
        Assert.Contains("COUNT(DISTINCT [region]) AS [regions]", sql);
    }

    [Fact]
    public void A_search_escapes_the_bracket_as_well_because_it_opens_a_set_in_like()
    {
        var plan = Plan(dims: [Dim("note")], filters: [new("note", FilterOperator.Contains, ["a[b]_50%\\"])]);

        var query = _renderer.Render(Dataset, plan);

        Assert.Contains("[note] LIKE @databoard_p0 ESCAPE '\\'", query.Sql);
        Assert.Equal(new object[] { "%a\\[b]\\_50\\%\\\\%" }, query.Parameters);
    }

    [Fact]
    public void A_bracket_in_a_name_cannot_end_the_name()
    {
        var query = _renderer.Render(Dataset, Plan(dims: [Dim("a]; DROP TABLE x; --", alias: "x]y")], limit: 1));

        Assert.Contains("[a]]; DROP TABLE x; --] AS [x]]y]", query.Sql);
    }

    [Fact]
    public void Never_puts_a_value_in_the_sql()
    {
        var plan = Plan(measures: [new PlannedMeasure(Aggregation.Count, null, "n")], filters: [new("region", FilterOperator.Eq, ["'; DROP TABLE x; --"])]);

        var query = _renderer.Render(Dataset, plan);

        Assert.DoesNotContain("DROP", query.Sql);
        Assert.Equal(new object[] { "'; DROP TABLE x; --" }, query.Parameters);
    }
}

public class MsSqlTableViewSqlRendererTests
{
    private readonly MsSqlTableViewSqlRenderer _renderer = new();

    private static TableViewPlan PlanWith(JoinType join) => new(
        [
            new PlannedTable(0, "dbo", "orders", null, []),
            new PlannedTable(1, "dbo", "cust]omers", join, [new PlannedCondition(0, "customer_id", "id")]),
        ],
        [new PlannedColumn(0, "id", "id"), new PlannedColumn(1, "name", "customer")]);

    [Fact]
    public void Quotes_every_name_with_brackets_and_doubles_a_closing_bracket_inside()
    {
        Assert.Equal(
            "SELECT t0.[id] AS [id],\n       t1.[name] AS [customer]\n" +
            "FROM [dbo].[orders] AS t0\n" +
            "LEFT JOIN [dbo].[cust]]omers] AS t1\n  ON t0.[customer_id] = t1.[id]",
            _renderer.Render(PlanWith(JoinType.Left)));
    }

    [Theory]
    [InlineData(JoinType.Inner, "INNER JOIN")]
    [InlineData(JoinType.Left, "LEFT JOIN")]
    [InlineData(JoinType.Right, "RIGHT JOIN")]
    [InlineData(JoinType.Full, "FULL JOIN")]
    public void Offers_every_kind_of_join(JoinType type, string keyword) => Assert.Contains(keyword, _renderer.Render(PlanWith(type)));
}

public class MsSqlConnectionPolicyTests
{
    private const string Valid = "Server=db.example.com,1433;Database=shop;User ID=reader;Password=pw;Encrypt=True";

    [Fact]
    public void Accepts_the_plain_options_and_their_aliases_and_finds_the_host()
    {
        var parsed = MsSqlConnectionPolicy.Parse(Valid);
        Assert.Equal("db.example.com", parsed.Host);
        Assert.Equal("shop", parsed.Builder.InitialCatalog);

        Assert.Equal("h", MsSqlConnectionPolicy.Parse("Data Source=h;Initial Catalog=d;Uid=u;Pwd=p").Host);
        Assert.Equal("h", MsSqlConnectionPolicy.Parse("Data Source=tcp:h,1433;Database=d;User=u;Password=p").Host);
        Assert.Equal("10.0.0.5", MsSqlConnectionPolicy.Parse("Server=10.0.0.5;Database=d;User ID=u;Password=p").Host);
    }

    [Theory]
    [InlineData("Integrated Security=True")]
    [InlineData("Trusted_Connection=Yes")]
    [InlineData("Authentication=Active Directory Default")]
    [InlineData("Authentication=ActiveDirectoryManagedIdentity")]
    [InlineData("AttachDbFilename=C:\\x.mdf")]
    [InlineData("User Instance=True")]
    [InlineData("Failover Partner=other")]
    [InlineData("Multi Subnet Failover=True")]
    [InlineData("Pooling=True")]
    [InlineData("Connect Timeout=300")]
    [InlineData("Command Timeout=0")]
    [InlineData("MultipleActiveResultSets=True")]
    [InlineData("Replication=True")]
    [InlineData("Workstation ID=x")]
    [InlineData("Column Encryption Setting=Enabled")]
    [InlineData("Server SPN=MSSQLSvc/x")]
    [InlineData("Host Name In Certificate=x")]
    public void Refuses_options_that_use_the_servers_identity_read_files_or_loosen_a_limit(string option)
    {
        var error = Assert.Throws<ValidationException>(() => MsSqlConnectionPolicy.Parse(Valid + ";" + option));

        Assert.Contains("not allowed", error.Errors["ConnectionString"][0]);
    }

    [Theory]
    [InlineData("Server=;Database=d;User ID=u")]
    [InlineData("Server=a,b;Database=d;User ID=u")]
    [InlineData("Server=a b;Database=d;User ID=u")]
    [InlineData("Server=.;Database=d;User ID=u")]
    [InlineData("Server=(local);Database=d;User ID=u")]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Database=d;User ID=u")]
    [InlineData("Server=host\\INSTANCE;Database=d;User ID=u")]
    [InlineData("Server=np:\\\\host\\pipe\\sql\\query;Database=d;User ID=u")]
    [InlineData("Server=lpc:host;Database=d;User ID=u")]
    [InlineData("Server=admin:host;Database=d;User ID=u")]
    [InlineData("Server=h,0;Database=d;User ID=u")]
    [InlineData("Server=h,70000;Database=d;User ID=u")]
    [InlineData("Server=h;Database=;User ID=u")]
    [InlineData("Server=h;Database=d;User ID=")]
    [InlineData("Server=h;Database=master;User ID=u")]
    [InlineData("Server=h;Database=MSDB;User ID=u")]
    [InlineData("Server=h;Database=tempdb;User ID=u")]
    [InlineData("Server=h;Database=d;User ID=u;Context Connection=True")]
    [InlineData("this is not a connection string")]
    public void Refuses_a_server_that_is_not_a_plain_host_or_a_missing_part_or_the_servers_own_databases(string connection) =>
        Assert.Throws<ValidationException>(() => MsSqlConnectionPolicy.Parse(connection));

    [Fact]
    public void Forces_the_safe_settings_on_top()
    {
        var hardened = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(MsSqlConnectionPolicy.Harden(MsSqlConnectionPolicy.Parse(Valid).Builder));

        Assert.False(hardened.Pooling);
        Assert.False(hardened.MultipleActiveResultSets);
        Assert.Equal(5, hardened.ConnectTimeout);
        Assert.Equal(15, hardened.CommandTimeout);
        Assert.Equal(0, hardened.ConnectRetryCount);
    }
}

public class MsSqlTypeNamesTests
{
    [Theory]
    [InlineData("bit", "boolean")]
    [InlineData("tinyint", "smallint")]
    [InlineData("smallint", "smallint")]
    [InlineData("int", "integer")]
    [InlineData("bigint", "bigint")]
    [InlineData("real", "real")]
    [InlineData("float", "double precision")]
    [InlineData("decimal", "numeric")]
    [InlineData("decimal(10,2)", "numeric")]
    [InlineData("money", "numeric")]
    [InlineData("date", "date")]
    [InlineData("datetime", "timestamp without time zone")]
    [InlineData("datetime2", "timestamp without time zone")]
    [InlineData("smalldatetime", "timestamp without time zone")]
    [InlineData("datetimeoffset", "timestamp with time zone")]
    [InlineData("time", "time without time zone")]
    [InlineData("nvarchar", "text")]
    [InlineData("varchar(50)", "text")]
    [InlineData("nchar", "text")]
    [InlineData("ntext", "text")]
    [InlineData("uniqueidentifier", "uuid")]
    [InlineData("varbinary", "bytea")]
    [InlineData("xml", "xml")]
    [InlineData("geography", "geography")]
    public void Gives_each_type_the_name_the_application_knows(string mssql, string expected) =>
        Assert.Equal(expected, MsSqlTypes.Canonical(mssql));
}
