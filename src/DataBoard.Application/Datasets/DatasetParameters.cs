using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;

namespace DataBoard.Application.Datasets;

public enum ParameterType { Text, Number, Date, Boolean }

/// <summary>
/// A value a dataset's SQL takes, written as <c>{{name}}</c> in the SQL. <paramref name="Default"/> is used wherever
/// nobody has chosen a value. It is a JSON string, number or boolean, whichever fits <paramref name="Type"/>.
/// </summary>
public sealed record DatasetParameter(string Name, ParameterType Type, JsonElement? Default = null, string? Label = null);

/// <summary>SQL ready to run: its <c>{{placeholders}}</c> replaced by <c>@databoard_p0</c>, ... and the values to bind to them.</summary>
public sealed record BoundSql(string Sql, IReadOnlyList<object?> Parameters);

public static class DatasetParametersJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string? ToJson(IReadOnlyList<DatasetParameter>? parameters) =>
        parameters is { Count: > 0 } ? JsonSerializer.Serialize(parameters, Options) : null;

    public static IReadOnlyList<DatasetParameter> FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<DatasetParameter>>(json, Options) ?? [];
}

/// <summary>
/// Dataset parameters. A parameter is never pasted into the SQL: each <c>{{name}}</c> becomes a bound parameter of the
/// database driver, so a value can only ever be data, whatever it contains. The SQL that remains is then checked by the
/// same read-only validator as any other. Placeholders are refused inside quotes and comments, where a bound parameter
/// would silently mean something else.
/// </summary>
public static partial class SqlParameters
{
    public const int MaxParameters = 10;
    public const int MaxTextLength = 1_000;
    public const int MaxLabelLength = 100;

    [GeneratedRegex(@"^\{\{\s*([A-Za-z_][A-Za-z0-9_]{0,62})\s*\}\}")]
    private static partial Regex PlaceholderAt();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex NamePattern();

    /// <summary>The names of the placeholders in a SQL text, once each, in the order they first appear.</summary>
    public static IReadOnlyList<string> Placeholders(string sql)
    {
        var names = new List<string>();
        Scan(sql, (_, _, name) =>
        {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        });
        return names;
    }

    /// <summary>
    /// Checks a dataset's parameter definitions against its SQL: valid and unique names, defaults that fit their type,
    /// every placeholder declared, every declaration used.
    /// </summary>
    public static void CheckDefinitions(string sql, IReadOnlyList<DatasetParameter>? declared)
    {
        declared ??= [];
        var placeholders = Placeholders(sql);

        if (declared.Count > MaxParameters)
            throw new ValidationException("Parameters", $"A dataset can have at most {MaxParameters} parameters.");

        for (var i = 0; i < declared.Count; i++)
        {
            var parameter = declared[i];
            var field = $"Parameters[{i}]";
            if (parameter is null || !NamePattern().IsMatch(parameter.Name ?? string.Empty))
                throw new ValidationException(field, "A parameter name starts with a letter or underscore and has only letters, digits and underscores.");
            if (!Enum.IsDefined(parameter.Type))
                throw new ValidationException(field, $"'{parameter.Name}' has an unknown type.");
            if (parameter.Label is { Length: > MaxLabelLength })
                throw new ValidationException(field, $"The label of '{parameter.Name}' must be at most {MaxLabelLength} characters.");
            if (parameter.Default is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value
                && !TryConvert(parameter.Type, value, out _, out var problem))
                throw new ValidationException(field, $"The default of '{parameter.Name}': {problem}.");
        }

        var duplicate = declared.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ValidationException("Parameters", $"The parameter '{duplicate.Key}' is declared more than once.");

        var known = declared.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var undeclared = placeholders.FirstOrDefault(p => !known.Contains(p));
        if (undeclared is not null)
            throw new ValidationException("Parameters", $"The SQL uses {{{{{undeclared}}}}}, which is not a declared parameter. Add it, with a type and a default.");

        var unused = declared.FirstOrDefault(p => !placeholders.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
        if (unused is not null)
            throw new ValidationException("Parameters", $"The parameter '{unused.Name}' is not used in the SQL. Write {{{{{unused.Name}}}}} where it belongs, or remove it.");
    }

    /// <summary>
    /// Replaces each placeholder with a bound parameter and returns the values to bind. A value given in
    /// <paramref name="values"/> wins over the parameter's default; a parameter with neither is an error.
    /// </summary>
    public static BoundSql Bind(string sql, IReadOnlyList<DatasetParameter> declared, IReadOnlyDictionary<string, JsonElement>? values)
    {
        var byName = declared.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        if (values is not null)
        {
            var unknown = values.Keys.FirstOrDefault(k => !byName.ContainsKey(k));
            if (unknown is not null)
                throw new ValidationException("Parameters", $"'{unknown}' is not a parameter of this dataset.");
        }

        var supplied = values is null ? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, JsonElement>(values, StringComparer.OrdinalIgnoreCase);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var bound = new List<object?>();
        var text = new StringBuilder();
        var last = 0;

        Scan(sql, (start, length, name) =>
        {
            text.Append(sql, last, start - last);
            last = start + length;

            if (!index.TryGetValue(name, out var position))
            {
                var parameter = byName.TryGetValue(name, out var found)
                    ? found
                    : throw new ValidationException("Parameters", $"The SQL uses {{{{{name}}}}}, which is not a declared parameter.");
                var raw = supplied.TryGetValue(parameter.Name, out var given) && IsPresent(given) ? given : parameter.Default;
                if (raw is not { } value || !IsPresent(value))
                    throw new ValidationException($"Parameters.{parameter.Name}", $"The parameter '{parameter.Name}' needs a value.");
                if (!TryConvert(parameter.Type, value, out var converted, out var problem))
                    throw new ValidationException($"Parameters.{parameter.Name}", $"'{parameter.Name}': {problem}.");

                position = bound.Count;
                index[name] = position;
                bound.Add(converted);
            }

            text.Append('@').Append(ParameterizedQuery.Name(position));
        });

        text.Append(sql, last, sql.Length - last);
        return new BoundSql(text.ToString(), bound);
    }

    private static bool IsPresent(JsonElement value) => value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static bool TryConvert(ParameterType type, JsonElement value, out object converted, out string problem)
    {
        converted = null!;
        problem = string.Empty;

        switch (type)
        {
            case ParameterType.Text:
                if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: <= MaxTextLength } text)
                {
                    converted = text;
                    return true;
                }
                problem = $"expected text of at most {MaxTextLength} characters";
                return false;

            case ParameterType.Number:
                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                {
                    converted = number;
                    return true;
                }
                if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    converted = number;
                    return true;
                }
                problem = "expected a number";
                return false;

            case ParameterType.Date:
                if (value.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    converted = date;
                    return true;
                }
                problem = "expected a date as yyyy-MM-dd";
                return false;

            case ParameterType.Boolean:
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    converted = value.GetBoolean();
                    return true;
                }
                if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var flag))
                {
                    converted = flag;
                    return true;
                }
                problem = "expected true or false";
                return false;

            default:
                problem = "unknown type";
                return false;
        }
    }

    // ---- finding the placeholders --------------------------------------------------------------------------------

    /// <summary>
    /// Walks the SQL the way PostgreSQL reads it, so a placeholder is only recognised where it can stand for a value:
    /// not inside a string, a quoted name, a dollar-quoted body or a comment. Calls <paramref name="found"/> with the
    /// start, the length and the name of each one.
    /// </summary>
    private static void Scan(string sql, Action<int, int, string> found)
    {
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];

            if (c == '\'' || c == '"')
            {
                i = SkipQuoted(sql, i, c);
            }
            else if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var end = sql.IndexOf('\n', i);
                CheckNoPlaceholder(sql, i, end < 0 ? sql.Length : end, "a comment");
                i = end < 0 ? sql.Length : end;
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = SkipBlockComment(sql, i);
                CheckNoPlaceholder(sql, i, end, "a comment");
                i = end;
            }
            else if (c == '$' && DollarTagAt(sql, i) is { } tag)
            {
                var close = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                var end = close < 0 ? sql.Length : close + tag.Length;
                CheckNoPlaceholder(sql, i, end, "a dollar-quoted string");
                i = end;
            }
            else if (c == '{' && i + 1 < sql.Length && sql[i + 1] == '{')
            {
                var match = PlaceholderAt().Match(sql[i..]);
                if (!match.Success)
                    throw new ValidationException("Sql", "A parameter is written {{name}}: two braces, a name made of letters, digits and underscores, two braces.");
                found(i, match.Length, match.Groups[1].Value);
                i += match.Length;
            }
            else
            {
                i++;
            }
        }
    }

    private static int SkipQuoted(string sql, int start, char quote)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == quote)
            {
                if (i + 1 < sql.Length && sql[i + 1] == quote) { i += 2; continue; } // '' or "" is a quote inside the quotes
                i++;
                CheckNoPlaceholder(sql, start, i, quote == '\'' ? "quotes" : "a quoted name");
                return i;
            }
            i++;
        }
        CheckNoPlaceholder(sql, start, sql.Length, "quotes");
        return sql.Length;
    }

    private static int SkipBlockComment(string sql, int start)
    {
        var depth = 0;
        var i = start;
        while (i < sql.Length - 1)
        {
            if (sql[i] == '/' && sql[i + 1] == '*') { depth++; i += 2; }
            else if (sql[i] == '*' && sql[i + 1] == '/') { depth--; i += 2; if (depth == 0) return i; }
            else i++;
        }
        return sql.Length;
    }

    /// <summary>"$tag$" or "$$" at this position, if it opens a dollar-quoted string.</summary>
    private static string? DollarTagAt(string sql, int start)
    {
        var i = start + 1;
        while (i < sql.Length && (char.IsAsciiLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
        if (i >= sql.Length || sql[i] != '$') return null;
        if (i > start + 1 && char.IsAsciiDigit(sql[start + 1])) return null; // $1 is a positional parameter, not a quote
        return sql[start..(i + 1)];
    }

    private static void CheckNoPlaceholder(string sql, int start, int end, string where)
    {
        var match = PlaceholderInside().Match(sql, start, end - start);
        if (match.Success)
            throw new ValidationException("Sql", $"{{{{{match.Groups[1].Value}}}}} is inside {where}, where it would not be a value. Write it without quotes, for example region = {{{{{match.Groups[1].Value}}}}}.");
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]{0,62})\s*\}\}")]
    private static partial Regex PlaceholderInside();
}
