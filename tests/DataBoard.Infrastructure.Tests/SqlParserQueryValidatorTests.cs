using DataBoard.Application.Common;
using DataBoard.Domain.DataSources;
using DataBoard.Infrastructure.DataSources;

namespace DataBoard.Infrastructure.Tests;

public class SqlParserQueryValidatorTests
{
    private readonly SqlParserQueryValidator _validator = new();

    private string Validate(string sql) => _validator.ValidateReadOnly(DataSourceType.PostgreSql, sql);

    private ValidationException Rejected(string sql) =>
        Assert.Throws<ValidationException>(() => Validate(sql));

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("select * from sales where amount > 10 order by sold_at desc limit 5")]
    [InlineData("WITH a AS (SELECT 1 AS x) SELECT * FROM a")]
    [InlineData("SELECT 1 UNION ALL SELECT 2")]
    [InlineData("SELECT region, sum(amount) FROM sales GROUP BY region HAVING sum(amount) > 100")]
    [InlineData("SELECT * FROM generate_series(1, 10) AS g")]
    [InlineData("SELECT s.* FROM sales s JOIN regions r ON r.id = s.region_id WHERE s.id IN (SELECT id FROM top_sales)")]
    [InlineData("SELECT 'a;b', \"weird;col\" FROM t")]
    [InlineData("SELECT 1 -- trailing comment")]
    [InlineData("SELECT 1 /* ; DROP TABLE x */")]
    [InlineData("SELECT now(), version(), upper('x'), coalesce(NULL, 1)")]
    public void Accepts_read_only_queries(string sql) => Assert.NotEmpty(Validate(sql));

    [Fact]
    public void Trailing_semicolons_and_whitespace_are_removed() =>
        Assert.Equal("SELECT 1", Validate("  SELECT 1 ;; \n"));

    [Theory]
    [InlineData("DROP TABLE x")]
    [InlineData("DELETE FROM t")]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("CREATE TABLE x (a int)")]
    [InlineData("ALTER TABLE t ADD COLUMN b int")]
    [InlineData("TRUNCATE t")]
    [InlineData("GRANT ALL ON t TO public")]
    [InlineData("COPY t TO PROGRAM 'ls'")]
    [InlineData("EXPLAIN ANALYZE DELETE FROM t")]
    [InlineData("SET ROLE admin")]
    [InlineData("BEGIN")]
    [InlineData("COMMIT")]
    [InlineData("CALL do_things()")]
    [InlineData("VACUUM")]
    public void Rejects_anything_that_is_not_a_select(string sql) => Rejected(sql);

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1; DROP TABLE x")]
    [InlineData("SELECT 1; -- c\nDELETE FROM t")]
    public void Rejects_more_than_one_statement(string sql) => Rejected(sql);

    [Fact]
    public void Rejects_data_modifying_common_table_expressions()
    {
        Rejected("WITH d AS (DELETE FROM t RETURNING *) SELECT * FROM d");
        Rejected("WITH i AS (INSERT INTO t VALUES (1) RETURNING *) SELECT * FROM i");
    }

    [Fact]
    public void Rejects_data_modifying_ctes_nested_in_other_ctes()
    {
        Rejected("WITH a AS (WITH i AS (INSERT INTO t VALUES (1) RETURNING *) SELECT * FROM i) SELECT * FROM a");
        Rejected("WITH a AS (SELECT 1), i AS (INSERT INTO t VALUES (1) RETURNING *) SELECT * FROM a");
    }

    [Fact]
    public void Syntax_the_parser_does_not_understand_is_refused_not_guessed_at()
    {
        // Valid PostgreSQL (column definition list), but not supported by the parser: fail closed.
        var error = Rejected("SELECT * FROM dblink('host=x', 'select 1') AS t(a int)");
        Assert.Contains("could not be parsed", error.Errors["Sql"][0]);
    }

    [Fact]
    public void Rejects_select_into_and_row_locks()
    {
        Assert.Contains("INTO", Rejected("SELECT * INTO newt FROM t").Errors["Sql"][0]);
        Rejected("SELECT * FROM t FOR UPDATE");
        Rejected("SELECT * FROM t FOR SHARE");
        Rejected("SELECT 1 UNION SELECT * INTO x FROM t");
    }

    [Theory]
    [InlineData("SELECT pg_read_file('/etc/passwd')")]
    [InlineData("SELECT PG_READ_FILE('/etc/passwd')")]
    [InlineData("SELECT pg_catalog.pg_read_file('/etc/passwd')")]
    [InlineData("SELECT \"pg_read_file\"('/etc/passwd')")]
    [InlineData("SELECT pg_read_binary_file('/x')")]
    [InlineData("SELECT * FROM pg_ls_dir('/')")]
    [InlineData("SELECT pg_ls_logdir()")]
    [InlineData("SELECT pg_stat_file('/x')")]
    [InlineData("SELECT pg_sleep(30)")]
    [InlineData("SELECT pg_terminate_backend(1)")]
    [InlineData("SELECT pg_cancel_backend(1)")]
    [InlineData("SELECT pg_advisory_lock(1)")]
    [InlineData("SELECT lo_import('/etc/passwd')")]
    [InlineData("SELECT lo_export(1, '/tmp/x')")]
    [InlineData("SELECT * FROM dblink('host=x', 'select 1')")]
    [InlineData("SELECT set_config('log_statement', 'all', false)")]
    [InlineData("SELECT nextval('s')")]
    [InlineData("SELECT setval('s', 1)")]
    [InlineData("SELECT query_to_xml('select 1', true, true, '')")]
    [InlineData("SELECT * FROM t WHERE id IN (SELECT lo_import('x'))")]
    [InlineData("WITH a AS (SELECT pg_read_file('x')) SELECT * FROM a")]
    public void Rejects_dangerous_functions_wherever_they_appear(string sql)
    {
        var error = Rejected(sql);
        Assert.Contains("not allowed", error.Errors["Sql"][0]);
    }

    [Theory]
    [InlineData("SELECT * FROM pg_authid")]
    [InlineData("SELECT * FROM pg_catalog.pg_shadow")]
    [InlineData("SELECT * FROM pg_largeobject")]
    [InlineData("SELECT * FROM pg_hba_file_rules")]
    [InlineData("SELECT * FROM t WHERE a IN (SELECT rolpassword FROM pg_authid)")]
    public void Rejects_sensitive_system_relations(string sql) => Rejected(sql);

    [Fact]
    public void Rejects_empty_input()
    {
        Rejected("");
        Rejected("   ");
        Rejected(";");
    }

    [Fact]
    public void Rejects_oversized_input() =>
        Rejected("SELECT '" + new string('a', SqlParserQueryValidator.MaxSqlLength) + "'");

    [Fact]
    public void Rejects_deeply_nested_input_before_the_parser_can_run_out_of_stack() =>
        Rejected("SELECT " + new string('(', 200) + "1" + new string(')', 200));

    [Fact]
    public void Rejects_garbage() => Rejected("this is not sql at all");

    [Theory]
    [InlineData(DataSourceType.SqlServer)]
    [InlineData(DataSourceType.Csv)]
    public void Only_postgresql_is_supported(DataSourceType type) =>
        Assert.Throws<ValidationException>(() => _validator.ValidateReadOnly(type, "SELECT 1"));
}
