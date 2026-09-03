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

    // Matched against the last part of a function name, so pg_catalog.pg_read_file is caught too.
    [GeneratedRegex(
        "^(pg_read_file|pg_read_binary_file|pg_ls_.*|pg_stat_file|pg_sleep.*|pg_terminate_backend|pg_cancel_backend|" +
        "pg_reload_conf|pg_rotate_logfile|pg_switch_wal|pg_promote|pg_advisory.*|pg_try_advisory.*|pg_create_.*|" +
        "pg_drop_.*|pg_logical_.*|pg_replication_.*|pg_file_.*|pg_export_snapshot|pg_notify|pg_import_system_collations|" +
        "lo_.*|loread|lowrite|dblink.*|set_config|nextval|setval|.*_to_xml.*|xpath.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeniedFunction();

    private static readonly HashSet<string> DeniedRelations = new(StringComparer.OrdinalIgnoreCase)
    {
        "pg_authid", "pg_shadow", "pg_user_mappings", "pg_largeobject", "pg_hba_file_rules",
        "pg_file_settings", "pg_ident_file_mappings",
    };

    public string ValidateReadOnly(DataSourceType type, string sql)
    {
        if (type != DataSourceType.PostgreSql)
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
            statements = new SqlQueryParser().Parse(sql, new PostgreSqlDialect());
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

        var guard = new GuardVisitor();
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

    private sealed class GuardVisitor : Visitor
    {
        public List<string> Violations { get; } = [];

        public override ControlFlow PreVisitExpression(Expression expression)
        {
            if (expression is Expression.Function f) CheckFunction(f.Name);
            return ControlFlow.Continue;
        }

        public override ControlFlow PreVisitTableFactor(TableFactor tableFactor)
        {
            // A function used as a table, for example FROM pg_ls_dir('/').
            if (tableFactor is TableFactor.Table { Args: not null } table) CheckFunction(table.Name);
            return ControlFlow.Continue;
        }

        public override ControlFlow PreVisitRelation(ObjectName relation)
        {
            var name = relation.Values.LastOrDefault()?.Value;
            if (name is not null && DeniedRelations.Contains(name))
                Violations.Add($"Reading {name} is not allowed.");
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
            if (last is not null && DeniedFunction().IsMatch(last))
                Violations.Add($"The function {last} is not allowed.");
        }
    }
}
