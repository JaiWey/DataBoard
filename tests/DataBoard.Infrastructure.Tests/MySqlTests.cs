using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Domain.DataSources;
using DataBoard.Infrastructure.DataSources;

namespace DataBoard.Infrastructure.Tests;

public class MySqlQueryValidatorTests
{
    private readonly SqlParserQueryValidator _validator = new();

    private string Validate(string sql) => _validator.ValidateReadOnly(DataSourceType.MySql, sql);

    private ValidationException Rejected(string sql) => Assert.Throws<ValidationException>(() => Validate(sql));

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT `order id`, SUM(amount) FROM `shop`.`sales 2026` GROUP BY 1 HAVING SUM(amount) > 100")]
    [InlineData("SELECT * FROM sales WHERE amount > 10 ORDER BY sold_at DESC LIMIT 5")]
    [InlineData("SELECT * FROM sales LIMIT 5, 10")]
    [InlineData("WITH a AS (SELECT 1 AS x) SELECT * FROM a")]
    [InlineData("SELECT region FROM sales UNION SELECT region FROM refunds")]
    [InlineData("SELECT DATE_FORMAT(sold_at, '%Y-%m') AS month, COUNT(*) FROM sales GROUP BY month")]
    [InlineData("SELECT IFNULL(note, ''), CONCAT(a, b), JSON_EXTRACT(data, '$.a') FROM sales")]
    [InlineData("SELECT * FROM sales WHERE region = @databoard_p0 AND amount >= @databoard_p1")]
    [InlineData("SELECT * FROM sales WHERE note LIKE '%50\\%%' ESCAPE '\\\\'")]
    [InlineData("SELECT * FROM sales WHERE a <=> b")]
    [InlineData("SELECT * FROM user")] // a table of the person's own that happens to be called user
    public void Accepts_read_only_queries(string sql) => Assert.NotEmpty(Validate(sql));

    [Theory]
    [InlineData("DELETE FROM sales")]
    [InlineData("DROP TABLE sales")]
    [InlineData("UPDATE sales SET amount = 0")]
    [InlineData("INSERT INTO sales VALUES (1)")]
    [InlineData("REPLACE INTO sales VALUES (1)")]
    [InlineData("CREATE TABLE x (a int)")]
    [InlineData("TRUNCATE TABLE sales")]
    [InlineData("SET GLOBAL read_only = 0")]
    [InlineData("LOAD DATA INFILE '/etc/passwd' INTO TABLE sales")]
    [InlineData("CALL do_something()")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1; DROP TABLE sales")]
    public void Rejects_everything_that_is_not_one_select(string sql) => Rejected(sql);

    [Theory]
    [InlineData("SELECT * FROM sales INTO OUTFILE '/tmp/x'")]
    [InlineData("SELECT * FROM sales INTO DUMPFILE '/tmp/x'")]
    [InlineData("SELECT 1 INTO @x")]
    [InlineData("SELECT * FROM sales FOR UPDATE")]
    [InlineData("SELECT * FROM sales LOCK IN SHARE MODE")]
    public void Rejects_a_select_that_writes_or_locks(string sql) => Rejected(sql);

    [Theory]
    [InlineData("SELECT LOAD_FILE('/etc/passwd')", "LOAD_FILE")]
    [InlineData("SELECT SLEEP(100)", "SLEEP")]
    [InlineData("SELECT BENCHMARK(100000000, MD5('x'))", "BENCHMARK")]
    [InlineData("SELECT GET_LOCK('a', 100)", "GET_LOCK")]
    [InlineData("SELECT RELEASE_ALL_LOCKS()", "RELEASE_ALL_LOCKS")]
    [InlineData("SELECT sys_exec('id')", "sys_exec")]
    [InlineData("SELECT * FROM mysql.user", "mysql")]
    [InlineData("SELECT * FROM `mysql`.`user`", "mysql")]
    [InlineData("SELECT * FROM performance_schema.threads", "performance_schema")]
    [InlineData("SELECT * FROM sys.session", "sys")]
    public void Rejects_what_reaches_outside_the_data(string sql, string name) =>
        Assert.Contains(name, Rejected(sql).Errors["Sql"][0], StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("SELECT * FROM (SELECT LOAD_FILE('/etc/passwd')) AS t")]
    [InlineData("WITH x AS (SELECT SLEEP(5)) SELECT * FROM x")]
    [InlineData("SELECT * FROM sales WHERE region IN (SELECT LOAD_FILE('x'))")]
    [InlineData("SELECT (SELECT SLEEP(5)) AS leaked")]
    [InlineData("SELECT region FROM sales UNION ALL SELECT LOAD_FILE('x')")]
    [InlineData("SELECT * FROM sales WHERE EXISTS (SELECT 1 FROM mysql.user)")]
    [InlineData("SELECT * FROM sales s JOIN (SELECT GET_LOCK('a', 1) AS e) j ON 1 = 1")]
    [InlineData("SELECT * FROM sales s JOIN mysql.user u ON 1 = 1")]
    public void Looks_for_the_dangerous_part_inside_other_queries_too(string sql) => Rejected(sql);
}

public class MySqlChartSqlRendererTests
{
    private readonly MySqlChartSqlRenderer _renderer = new();
    private const string Dataset = "SELECT * FROM sales";

    private static ChartQueryPlan Plan(
        PlannedDimension[]? dims = null, PlannedMeasure[]? measures = null, PlannedFilter[]? filters = null,
        PlannedMeasureFilter[]? having = null, PlannedOrder[]? order = null, int limit = 100, int? seriesLimit = null) =>
        new(dims ?? [], measures ?? [], filters ?? [], having ?? [], order ?? [], limit, seriesLimit);

    private static PlannedDimension Dim(string column, string type = "text", TimeGrain? grain = null, string? alias = null) =>
        new(column, type, grain, alias ?? column);

    [Fact]
    public void Renders_a_full_grouped_query_with_backticks_and_a_limit()
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
            "SELECT `region` AS `region`, SUM(`price`) AS `revenue`\n" +
            "FROM (\nSELECT * FROM sales\n) AS `databoard_dataset`\n" +
            "WHERE `region` IN (@databoard_p0, @databoard_p1) AND `quantity` >= @databoard_p2\n" +
            "GROUP BY 1\n" +
            "HAVING SUM(`price`) > @databoard_p3\n" +
            "ORDER BY 2 DESC\n" +
            "LIMIT 11",
            query.Sql);
        Assert.Equal(new object[] { "Auckland", "Wellington", 3m, 1000m }, query.Parameters);
    }

    [Fact]
    public void A_series_limit_ranks_with_is_null_in_place_of_nulls_last_and_compares_with_the_null_safe_operator()
    {
        var plan = Plan(
            dims: [Dim("at", "date", TimeGrain.Month, "month"), Dim("region")],
            measures: [new PlannedMeasure(Aggregation.Sum, "price", "revenue")],
            order: [new(1, SortDirection.Asc)],
            limit: 10,
            seriesLimit: 5);

        var query = _renderer.Render(Dataset, plan);

        Assert.Equal(
            "WITH `databoard_base` AS (\n" +
            "SELECT DATE_SUB(DATE(`at`), INTERVAL DAYOFMONTH(`at`) - 1 DAY) AS `month`, `region` AS `region`, SUM(`price`) AS `revenue`\n" +
            "FROM (\nSELECT * FROM sales\n) AS `databoard_dataset`\n" +
            "GROUP BY 1, 2\n" +
            "), `databoard_top_series` AS (\n" +
            "SELECT `region` FROM `databoard_base` GROUP BY 1 ORDER BY SUM(`revenue`) IS NULL, SUM(`revenue`) DESC, 1 LIMIT 5\n" +
            ")\n" +
            "SELECT b.* FROM `databoard_base` AS b WHERE EXISTS (SELECT 1 FROM `databoard_top_series` AS t WHERE t.`region` <=> b.`region`)\n" +
            "ORDER BY 1 ASC\n" +
            "LIMIT 11",
            query.Sql);
    }

    [Theory]
    [InlineData(TimeGrain.Day, "DATE(`at`)")]
    [InlineData(TimeGrain.Month, "DATE_SUB(DATE(`at`), INTERVAL DAYOFMONTH(`at`) - 1 DAY)")]
    [InlineData(TimeGrain.Quarter, "DATE_ADD(MAKEDATE(YEAR(`at`), 1), INTERVAL QUARTER(`at`) - 1 QUARTER)")]
    [InlineData(TimeGrain.Year, "MAKEDATE(YEAR(`at`), 1)")]
    [InlineData(TimeGrain.Week, "DATE_SUB(DATE(`at`), INTERVAL WEEKDAY(`at`) DAY)")]
    public void Cuts_a_time_to_a_grain_with_mysql_date_functions(TimeGrain grain, string expected)
    {
        var query = _renderer.Render(Dataset, Plan(dims: [Dim("at", "date", grain, "d")], limit: 5));

        Assert.StartsWith($"SELECT {expected} AS `d`", query.Sql);
    }

    [Fact]
    public void Writes_a_search_with_the_backslash_escape_the_way_mysql_reads_it_and_binds_the_pattern()
    {
        var plan = Plan(dims: [Dim("note")], filters: [new("note", FilterOperator.Contains, ["50%_off\\"])]);

        var query = _renderer.Render(Dataset, plan);

        Assert.Contains("`note` LIKE @databoard_p0 ESCAPE '\\\\'", query.Sql);
        Assert.Equal(new object[] { "%50\\%\\_off\\\\%" }, query.Parameters);
    }

    [Fact]
    public void A_backtick_in_a_name_cannot_end_the_name()
    {
        var query = _renderer.Render(Dataset, Plan(dims: [Dim("a`; DROP TABLE x; --", alias: "x`y")], limit: 1));

        Assert.StartsWith("SELECT `a``; DROP TABLE x; --` AS `x``y`", query.Sql);
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

public class MySqlTableViewSqlRendererTests
{
    private readonly MySqlTableViewSqlRenderer _renderer = new();

    private static TableViewPlan PlanWith(JoinType join) => new(
        [
            new PlannedTable(0, "shop", "orders", null, []),
            new PlannedTable(1, "shop", "cust`omers", join, [new PlannedCondition(0, "customer_id", "id")]),
        ],
        [new PlannedColumn(0, "id", "id"), new PlannedColumn(1, "name", "customer")]);

    [Fact]
    public void Quotes_every_name_with_backticks_and_doubles_a_backtick_inside()
    {
        var sql = _renderer.Render(PlanWith(JoinType.Left));

        Assert.Equal(
            "SELECT t0.`id` AS `id`,\n       t1.`name` AS `customer`\n" +
            "FROM `shop`.`orders` AS t0\n" +
            "LEFT JOIN `shop`.`cust``omers` AS t1\n  ON t0.`customer_id` = t1.`id`",
            sql);
    }

    [Theory]
    [InlineData(JoinType.Inner, "INNER JOIN")]
    [InlineData(JoinType.Left, "LEFT JOIN")]
    [InlineData(JoinType.Right, "RIGHT JOIN")]
    public void Offers_the_joins_mysql_has(JoinType type, string keyword) => Assert.Contains(keyword, _renderer.Render(PlanWith(type)));

    [Fact]
    public void Says_so_in_words_when_a_full_join_is_asked_for()
    {
        var error = Assert.Throws<ValidationException>(() => _renderer.Render(PlanWith(JoinType.Full)));

        Assert.Contains("no full outer join", error.Errors["Definition"][0]);
    }
}

public class MySqlConnectionPolicyTests
{
    private const string Valid = "Server=db.example.com;Port=3306;Database=shop;User ID=reader;Password=pw;SSL Mode=Required";

    [Fact]
    public void Accepts_the_plain_options_and_their_aliases()
    {
        var builder = MySqlConnectionPolicy.Parse(Valid);
        Assert.Equal("db.example.com", builder.Server);
        Assert.Equal("shop", builder.Database);

        Assert.Equal("reader", MySqlConnectionPolicy.Parse("Host=h;Database=d;Username=reader;Password=p").UserID);
        Assert.Equal("reader", MySqlConnectionPolicy.Parse("Data Source=h;Initial Catalog=d;Uid=reader;Pwd=p").UserID);
    }

    [Theory]
    [InlineData("AllowLoadLocalInfile=true")]
    [InlineData("Allow User Variables=true")]
    [InlineData("SslCert=/etc/ssl/key.pem")]
    [InlineData("SslKey=/etc/ssl/key.pem")]
    [InlineData("CertificateFile=/etc/ssl/key.pfx")]
    [InlineData("SslCa=/etc/ssl/ca.pem")]
    [InlineData("Pooling=true")]
    [InlineData("Connection Protocol=Unix")]
    [InlineData("Pipe Name=mysql")]
    [InlineData("Allow Public Key Retrieval=true")]
    [InlineData("Connection Timeout=3000")]
    [InlineData("Default Command Timeout=0")]
    [InlineData("ServerRSAPublicKeyFile=/etc/key")]
    [InlineData("Load Balance=Random")]
    public void Refuses_options_that_read_files_change_the_session_or_loosen_a_limit(string option)
    {
        var error = Assert.Throws<ValidationException>(() => MySqlConnectionPolicy.Parse(Valid + ";" + option));

        Assert.Contains("not allowed", error.Errors["ConnectionString"][0]);
    }

    [Theory]
    [InlineData("Server=;Database=d;User ID=u")]
    [InlineData("Server=a,b;Database=d;User ID=u")]
    [InlineData("Server=a b;Database=d;User ID=u")]
    [InlineData("Server=/var/run/mysqld.sock;Database=d;User ID=u")]
    [InlineData("Server=h;Database=;User ID=u")]
    [InlineData("Server=h;Database=d;User ID=")]
    [InlineData("Server=h;Database=d;User ID=u;Port=0")]
    [InlineData("Server=h;Database=mysql;User ID=u")]
    [InlineData("Server=h;Database=SYS;User ID=u")]
    [InlineData("Server=h;Database=performance_schema;User ID=u")]
    [InlineData("this is not a connection string")]
    public void Refuses_a_connection_that_is_missing_something_or_aimed_at_the_servers_own_data(string connection) =>
        Assert.Throws<ValidationException>(() => MySqlConnectionPolicy.Parse(connection));

    [Fact]
    public void Forces_the_safe_settings_on_top()
    {
        var hardened = new MySqlConnector.MySqlConnectionStringBuilder(MySqlConnectionPolicy.Harden(MySqlConnectionPolicy.Parse(Valid)));

        Assert.False(hardened.Pooling);
        Assert.False(hardened.AllowLoadLocalInfile);
        Assert.False(hardened.AllowUserVariables);
        Assert.Equal(5u, hardened.ConnectionTimeout);
        Assert.Equal(15u, hardened.DefaultCommandTimeout);
    }
}

public class MySqlTypeNamesTests
{
    [Theory]
    [InlineData("TINYINT", "integer")]
    [InlineData("SMALLINT", "integer")]
    [InlineData("MEDIUMINT", "integer")]
    [InlineData("INT", "integer")]
    [InlineData("int unsigned", "integer")]
    [InlineData("BIGINT", "bigint")]
    [InlineData("BIGINT UNSIGNED", "bigint")]
    [InlineData("BOOL", "boolean")]
    [InlineData("FLOAT", "real")]
    [InlineData("DOUBLE", "double precision")]
    [InlineData("DECIMAL", "numeric")]
    [InlineData("decimal(10,2)", "numeric")]
    [InlineData("DATE", "date")]
    [InlineData("DATETIME", "timestamp without time zone")]
    [InlineData("TIMESTAMP", "timestamp without time zone")]
    [InlineData("TIME", "time without time zone")]
    [InlineData("VARCHAR", "text")]
    [InlineData("varchar(255)", "text")]
    [InlineData("CHAR", "text")]
    [InlineData("LONGTEXT", "text")]
    [InlineData("ENUM", "text")]
    [InlineData("SET", "text")]
    [InlineData("JSON", "json")]
    [InlineData("BLOB", "bytea")]
    [InlineData("VARBINARY", "bytea")]
    [InlineData("GEOMETRY", "geometry")]
    public void Gives_each_type_the_name_the_application_knows(string mysql, string expected) =>
        Assert.Equal(expected, MySqlTypes.Canonical(mysql));

    [Theory]
    [InlineData("integer", ColumnKind.Numeric)]
    [InlineData("bigint", ColumnKind.Numeric)]
    [InlineData("real", ColumnKind.Numeric)]
    [InlineData("numeric", ColumnKind.Numeric)]
    [InlineData("boolean", ColumnKind.Boolean)]
    [InlineData("date", ColumnKind.Date)]
    [InlineData("timestamp without time zone", ColumnKind.Timestamp)]
    [InlineData("text", ColumnKind.Text)]
    [InlineData("json", ColumnKind.Other)]
    public void And_the_application_reads_those_names_as_the_right_kind(string name, ColumnKind kind) =>
        Assert.Equal(kind, ColumnKinds.Of(name));
}
