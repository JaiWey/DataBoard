using System.Text.RegularExpressions;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Domain.DataSources;
using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// First line of defence for user-written SQL: it must parse as exactly one read-only query, and it must not call
/// functions or read tables that can touch the server's files, sessions or other queries. The second line is the
/// connector, which runs the query in a read-only transaction with a statement timeout. The third, which no code
/// can replace, is a database account that only has SELECT on the data it should see.
/// </summary>
internal sealed partial class SqlParserQueryValidator : ISqlQueryValidator
{
    public const int MaxSqlLength = 20_000;
    private const int MaxNesting = 64;

    /// <summary>What is parsed and what is refused for one kind of database.</summary>
    /// <param name="DeniedFunction">Matched against the last part of a function name, so a schema-qualified name is caught too.</param>
    /// <param name="DeniedRelations">Tables and views that expose the server itself (accounts, files, other sessions).</param>
    /// <param name="DeniedSchemas">Schemas (in MySQL, databases) that hold the server's own accounts and statistics, not the person's data.</param>
    private sealed record DialectRules(Dialect Dialect, Regex DeniedFunction, HashSet<string> DeniedRelations, HashSet<string>? DeniedSchemas = null);

    private static Regex Functions(string pattern) => new($"^({pattern})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static HashSet<string> Relations(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<DataSourceType, DialectRules> Rules = new()
    {
        [DataSourceType.PostgreSql] = new(
            new PostgreSqlDialect(),
            Functions(
                "pg_read_file|pg_read_binary_file|pg_ls_.*|pg_stat_file|pg_sleep.*|pg_terminate_backend|pg_cancel_backend|" +
                "pg_reload_conf|pg_rotate_logfile|pg_switch_wal|pg_promote|pg_advisory.*|pg_try_advisory.*|pg_create_.*|" +
                "pg_drop_.*|pg_logical_.*|pg_replication_.*|pg_file_.*|pg_export_snapshot|pg_notify|pg_import_system_collations|" +
                "lo_.*|loread|lowrite|dblink.*|set_config|nextval|setval|.*_to_xml.*|xpath.*"),
            Relations("pg_authid", "pg_shadow", "pg_user_mappings", "pg_largeobject", "pg_hba_file_rules",
                "pg_file_settings", "pg_ident_file_mappings")),

        // An uploaded CSV lives in a small SQLite file of its own, opened read-only. Nothing in it is secret; what is refused
        // is whatever could reach outside it: loading code, reading or writing files.
        [DataSourceType.Csv] = new(
            new SQLiteDialect(),
            Functions("load_extension|readfile|writefile|edit|fts3_tokenizer|zipfile.*|sqlite_.*dbpage.*"),
            Relations("sqlite_dbpage", "dbstat")),

        // Whatever a MySQL account may SELECT is its owner's decision. What is refused here is what reaches outside the data:
        // reading files, waiting, taking locks that other sessions feel, and running other code or other queries.
        [DataSourceType.MySql] = new(
            new MySqlDialect(),
            Functions(
                "load_file|sleep|benchmark|get_lock|release_lock|release_all_locks|is_free_lock|is_used_lock|" +
                "master_pos_wait|source_pos_wait|wait_for_executed_gtid_set|wait_until_sql_thread_after_gtids|" +
                "sys_exec|sys_eval|sys_get|sys_set|lib_mysqludf_.*|audit_log_.*|keyring_.*|group_replication_.*"),
            Relations(), // a table called "user" is a person's own business; the server's accounts are in the mysql schema, below
            Relations("mysql", "performance_schema", "sys")),
    };

    public string ValidateReadOnly(DataSourceType type, string sql)
    {
        if (!Rules.TryGetValue(type, out var rules))
            throw new ValidationException("Type", $"{type} data sources are not supported yet.");

        if (string.IsNullOrWhiteSpace(sql))
            throw new ValidationException("Sql", "SQL is required.");
        if (sql.Length > MaxSqlLength)
            throw new ValidationException("Sql", $"SQL must be at most {MaxSqlLength} characters.");
        if (MaxParenDepth(sql) > MaxNesting)
            throw new ValidationException("Sql", "SQL is nested too deeply.");

        Sequence<Statement> statements;
        try
        {
            statements = new SqlQueryParser().Parse(sql, rules.Dialect);
        }
        catch (Exception ex)
        {
            // Fail closed: anything the parser cannot understand is refused, not guessed at.
            throw new ValidationException("Sql", $"The SQL could not be parsed: {ex.Message}");
        }

        if (statements.Count != 1)
            throw new ValidationException("Sql", "Exactly one SQL statement is allowed.");

        if (statements[0] is not Statement.Select select)
            throw new ValidationException("Sql", "Only SELECT queries are allowed.");

        var guard = new GuardVisitor(rules);
        CheckQuery(select.Query, guard);
        ((IElement)statements[0]).Visit(guard);

        if (guard.Violations.Count > 0)
            throw new ValidationException("Sql", guard.Violations.Distinct().First());

        return sql.Trim().TrimEnd(';').TrimEnd();
    }

    private static void CheckQuery(Query query, GuardVisitor guard)
    {
        if (query.Locks is { Count: > 0 })
            guard.Violations.Add("Row locking clauses such as FOR UPDATE are not allowed.");
        CheckBody(query.Body, guard);

        // The visitor does not enter CTEs, so a data-modifying WITH (INSERT ... RETURNING) must be found here.
        if (query.With?.CteTables is { } ctes)
        {
            foreach (var cte in ctes)
                CheckQuery(cte.Query, guard);
        }
    }

    private static void CheckBody(SetExpression body, GuardVisitor guard)
    {
        switch (body)
        {
            case SetExpression.SelectExpression { Select.Into: not null }:
                guard.Violations.Add("SELECT ... INTO is not allowed.");
                break;
            case SetExpression.SetOperation op:
                CheckBody(op.Left, guard);
                CheckBody(op.Right, guard);
                break;
            case SetExpression.QueryExpression nested:
                CheckQuery(nested.Query, guard);
                break;
            case SetExpression.Insert:
                guard.Violations.Add("Data-modifying statements are not allowed.");
                break;
        }
    }

    private static int MaxParenDepth(string sql)
    {
        int depth = 0, max = 0;
        foreach (var c in sql)
        {
            if (c == '(') max = Math.Max(max, ++depth);
            else if (c == ')' && depth > 0) depth--;
        }
        return max;
    }

    private sealed class GuardVisitor(DialectRules rules) : Visitor
    {
        public List<string> Violations { get; } = [];

        public override ControlFlow PreVisitExpression(Expression expression)
        {
            if (expression is Expression.Function f) CheckFunction(f.Name);
            return ControlFlow.Continue;
        }

        public override ControlFlow PreVisitTableFactor(TableFactor tableFactor)
        {
            switch (tableFactor)
            {
                // A function used as a table, for example FROM pg_ls_dir('/').
                case TableFactor.Table { Args: not null } table:
                    CheckFunction(table.Name);
                    break;

                // A subquery used as a table, for example FROM (SELECT pg_read_file('/etc/passwd')) AS t. The visitor does not
                // go into it by itself, so it is checked here, the same as a subquery anywhere else (this covers LATERAL too).
                case TableFactor.Derived derived:
                    CheckQuery(derived.SubQuery, this);
                    ((IElement)derived.SubQuery).Visit(this);
                    break;

                // A join in parentheses, for example FROM (a JOIN (SELECT ...) b ON ...): what is inside is not entered either.
                case TableFactor.NestedJoin nested:
                    ((IElement)nested.TableWithJoins).Visit(this);
                    break;
            }
            return ControlFlow.Continue;
        }

        public override ControlFlow PreVisitRelation(ObjectName relation)
        {
            var name = relation.Values.LastOrDefault()?.Value;
            if (name is not null && rules.DeniedRelations.Contains(name))
                Violations.Add($"Reading {name} is not allowed.");

            // mysql.user, sys.session: the parts before the table's own name say which schema it is in.
            if (rules.DeniedSchemas is { } schemas)
            {
                foreach (var part in relation.Values.SkipLast(1))
                {
                    if (schemas.Contains(part.Value))
                        Violations.Add($"Reading from {part.Value} is not allowed.");
                }
            }
            return ControlFlow.Continue;
        }

        public override ControlFlow PreVisitQuery(Query query)
        {
            CheckQuery(query, this);
            return ControlFlow.Continue;
        }

        private void CheckFunction(ObjectName name)
        {
            var last = name.Values.LastOrDefault()?.Value;
            if (last is not null && rules.DeniedFunction.IsMatch(last))
                Violations.Add($"The function {last} is not allowed.");
        }
    }
}
