using System.Text.Json;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;

namespace DataBoard.Application.Tests;

public class SqlParametersTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static DatasetParameter Text(string name, string? @default = null) =>
        new(name, ParameterType.Text, @default is null ? null : J($"\"{@default}\""));

    private static DatasetParameter Number(string name, string? @default = null) =>
        new(name, ParameterType.Number, @default is null ? null : J(@default));

    private static IReadOnlyDictionary<string, JsonElement> Values(params (string Name, string Json)[] values) =>
        values.ToDictionary(v => v.Name, v => J(v.Json));

    private static ValidationException Rejects(Action action) => Assert.Throws<ValidationException>(action);

    // ---- finding placeholders ----------------------------------------------------------------------------------

    [Fact]
    public void Finds_each_placeholder_once_in_the_order_it_first_appears()
    {
        var names = SqlParameters.Placeholders("SELECT * FROM t WHERE a = {{region}} AND b > {{ min_price }} OR c = {{region}} AND d = {{REGION}}");

        Assert.Equal(["region", "min_price"], names);
    }

    [Fact]
    public void Finds_nothing_in_sql_without_placeholders() =>
        Assert.Empty(SqlParameters.Placeholders("SELECT 1 AS \"{x}\", '{1,2}'::text, ARRAY[1,2]"));

    [Theory]
    [InlineData("SELECT * FROM t WHERE a = '{{region}}'", "quotes")]
    [InlineData("SELECT * FROM t WHERE a = 'it''s {{region}}'", "quotes")]
    [InlineData("SELECT * FROM t WHERE \"{{region}}\" = 1", "a quoted name")]
    [InlineData("SELECT 1 -- {{region}}", "a comment")]
    [InlineData("SELECT 1 /* {{region}} */", "a comment")]
    [InlineData("SELECT 1 /* outer /* inner */ {{region}} */", "a comment")]
    [InlineData("SELECT $$ {{region}} $$", "a dollar-quoted string")]
    [InlineData("SELECT $tag$ {{region}} $tag$", "a dollar-quoted string")]
    public void Refuses_a_placeholder_where_it_would_not_be_a_value(string sql, string where)
    {
        var error = Rejects(() => SqlParameters.Placeholders(sql));

        Assert.Contains("{{region}}", error.Errors["Sql"][0]);
        Assert.Contains(where, error.Errors["Sql"][0]);
    }

    [Theory]
    [InlineData("SELECT * FROM t WHERE a = {{}}")]
    [InlineData("SELECT * FROM t WHERE a = {{1x}}")]
    [InlineData("SELECT * FROM t WHERE a = {{ two words }}")]
    [InlineData("SELECT * FROM t WHERE a = {{region")]
    [InlineData("SELECT * FROM t WHERE a = {{{region}}}")]
    public void Refuses_a_placeholder_that_is_not_written_correctly(string sql) =>
        Assert.Contains("{{name}}", Rejects(() => SqlParameters.Placeholders(sql)).Errors["Sql"][0]);

    [Fact]
    public void A_positional_parameter_is_not_mistaken_for_a_dollar_quote() =>
        Assert.Equal(["a"], SqlParameters.Placeholders("SELECT $1, {{a}}, $2"));

    [Fact]
    public void Quotes_closed_properly_do_not_hide_what_follows()
    {
        var names = SqlParameters.Placeholders("SELECT 'a', \"b\", 'it''s' FROM t WHERE x = {{after}}");

        Assert.Equal(["after"], names);
    }

    // ---- the definitions ---------------------------------------------------------------------------------------

    [Fact]
    public void Accepts_definitions_that_match_the_sql() =>
        SqlParameters.CheckDefinitions("SELECT * FROM t WHERE a = {{region}} AND b > {{min}}", [Text("region", "Auckland"), Number("min", "10")]);

    [Fact]
    public void Accepts_sql_with_no_parameters() =>
        SqlParameters.CheckDefinitions("SELECT 1", null);

    [Fact]
    public void Wants_every_placeholder_declared()
    {
        var error = Rejects(() => SqlParameters.CheckDefinitions("SELECT {{a}}, {{b}}", [Text("a", "x")]));

        Assert.Contains("{{b}}", error.Errors["Parameters"][0]);
        Assert.Contains("not a declared parameter", error.Errors["Parameters"][0]);
    }

    [Fact]
    public void Wants_every_declaration_used()
    {
        var error = Rejects(() => SqlParameters.CheckDefinitions("SELECT {{a}}", [Text("a", "x"), Text("typo", "y")]));

        Assert.Contains("'typo' is not used", error.Errors["Parameters"][0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1abc")]
    [InlineData("has space")]
    [InlineData("a-b")]
    public void Wants_a_plain_name(string name) =>
        Rejects(() => SqlParameters.CheckDefinitions("SELECT 1", [Text(name, "x")]));

    [Fact]
    public void Refuses_the_same_name_twice_ignoring_case() =>
        Assert.Contains("more than once", Rejects(() => SqlParameters.CheckDefinitions("SELECT {{a}}", [Text("a", "x"), Text("A", "y")])).Errors["Parameters"][0]);

    [Fact]
    public void Limits_how_many_parameters_a_dataset_has()
    {
        var many = Enumerable.Range(0, SqlParameters.MaxParameters + 1).Select(i => Text($"p{i}", "x")).ToList();
        var sql = "SELECT " + string.Join(", ", many.Select(p => $"{{{{{p.Name}}}}}"));

        Rejects(() => SqlParameters.CheckDefinitions(sql, many));
    }

    [Theory]
    [InlineData(ParameterType.Number, "\"abc\"")]
    [InlineData(ParameterType.Date, "\"15/01/2026\"")]
    [InlineData(ParameterType.Boolean, "\"maybe\"")]
    [InlineData(ParameterType.Text, "5")]
    public void Wants_a_default_that_fits_the_type(ParameterType type, string json) =>
        Assert.Contains("default", Rejects(() => SqlParameters.CheckDefinitions("SELECT {{a}}", [new("a", type, J(json))])).Errors.Single().Value[0]);

    [Fact]
    public void A_parameter_may_have_no_default_so_that_whoever_uses_it_has_to_choose() =>
        SqlParameters.CheckDefinitions("SELECT {{a}}", [Text("a")]);

    // ---- binding -----------------------------------------------------------------------------------------------

    [Fact]
    public void Replaces_each_placeholder_with_a_bound_parameter_and_returns_the_values()
    {
        var bound = SqlParameters.Bind(
            "SELECT * FROM t WHERE a = {{region}} AND b > {{ min }}", [Text("region", "Auckland"), Number("min", "10")], null);

        Assert.Equal("SELECT * FROM t WHERE a = @databoard_p0 AND b > @databoard_p1", bound.Sql);
        Assert.Equal(new object?[] { "Auckland", 10m }, bound.Parameters);
    }

    [Fact]
    public void A_parameter_used_twice_is_bound_once()
    {
        var bound = SqlParameters.Bind("SELECT {{a}}, {{b}}, {{A}}", [Text("a", "x"), Text("b", "y")], null);

        Assert.Equal("SELECT @databoard_p0, @databoard_p1, @databoard_p0", bound.Sql);
        Assert.Equal(new object?[] { "x", "y" }, bound.Parameters);
    }

    [Fact]
    public void Leaves_sql_without_placeholders_exactly_as_it_was()
    {
        const string sql = "SELECT 'a {not} one', \"b\" FROM t -- {x}\nWHERE a = 1";

        var bound = SqlParameters.Bind(sql, [], null);

        Assert.Equal(sql, bound.Sql);
        Assert.Empty(bound.Parameters);
    }

    [Fact]
    public void A_value_that_is_given_wins_over_the_default_and_the_name_is_matched_ignoring_case()
    {
        var bound = SqlParameters.Bind("SELECT {{region}}", [Text("region", "Auckland")], Values(("REGION", "\"Dunedin\"")));

        Assert.Equal(new object?[] { "Dunedin" }, bound.Parameters);
    }

    [Fact]
    public void An_empty_value_falls_back_to_the_default()
    {
        var bound = SqlParameters.Bind("SELECT {{region}}", [Text("region", "Auckland")], Values(("region", "null")));

        Assert.Equal(new object?[] { "Auckland" }, bound.Parameters);
    }

    [Fact]
    public void A_parameter_with_no_value_and_no_default_is_an_error_that_names_it()
    {
        var error = Rejects(() => SqlParameters.Bind("SELECT {{region}}", [Text("region")], null));

        Assert.Contains("'region' needs a value", error.Errors["Parameters.region"][0]);
    }

    [Fact]
    public void A_value_for_a_parameter_the_dataset_does_not_have_is_an_error()
    {
        var error = Rejects(() => SqlParameters.Bind("SELECT {{a}}", [Text("a", "x")], Values(("b", "\"y\""))));

        Assert.Contains("'b' is not a parameter", error.Errors["Parameters"][0]);
    }

    [Theory]
    [InlineData(ParameterType.Number, "12.5", 12.5)]
    [InlineData(ParameterType.Number, "\"12.5\"", 12.5)]
    [InlineData(ParameterType.Number, "-3", -3)]
    public void Converts_numbers_given_as_a_number_or_as_text(ParameterType type, string json, double expected) =>
        Assert.Equal((decimal)expected, SqlParameters.Bind("SELECT {{a}}", [new("a", type)], Values(("a", json))).Parameters[0]);

    [Fact]
    public void Converts_dates_and_booleans_to_their_types()
    {
        var bound = SqlParameters.Bind(
            "SELECT {{d}}, {{f}}, {{g}}",
            [new("d", ParameterType.Date), new("f", ParameterType.Boolean), new("g", ParameterType.Boolean)],
            Values(("d", "\"2026-03-05\""), ("f", "true"), ("g", "\"false\"")));

        Assert.Equal(new object?[] { new DateOnly(2026, 3, 5), true, false }, bound.Parameters);
    }

    [Theory]
    [InlineData(ParameterType.Number, "\"lots\"")]
    [InlineData(ParameterType.Number, "true")]
    [InlineData(ParameterType.Date, "\"2026-02-30\"")]
    [InlineData(ParameterType.Date, "20260305")]
    [InlineData(ParameterType.Boolean, "1")]
    [InlineData(ParameterType.Text, "{\"a\":1}")]
    public void A_value_of_the_wrong_type_is_an_error_that_names_the_parameter(ParameterType type, string json)
    {
        var error = Rejects(() => SqlParameters.Bind("SELECT {{a}}", [new("a", type)], Values(("a", json))));

        Assert.StartsWith("'a':", error.Errors["Parameters.a"][0]);
    }

    [Fact]
    public void Limits_how_long_a_text_value_can_be() =>
        Rejects(() => SqlParameters.Bind("SELECT {{a}}", [Text("a")], Values(("a", $"\"{new string('x', SqlParameters.MaxTextLength + 1)}\""))));

    [Theory]
    [InlineData("'; DROP TABLE sales; --")]
    [InlineData("x' OR '1'='1")]
    [InlineData("{{other}}")]
    [InlineData("$$; SELECT pg_sleep(100); $$")]
    [InlineData("@databoard_p0")]
    public void A_value_is_data_and_never_becomes_part_of_the_sql(string hostile)
    {
        var bound = SqlParameters.Bind(
            "SELECT * FROM t WHERE a = {{a}} AND b = {{other}}", [Text("a"), Text("other", "ok")], Values(("a", JsonSerializer.Serialize(hostile))));

        Assert.Equal("SELECT * FROM t WHERE a = @databoard_p0 AND b = @databoard_p1", bound.Sql);
        Assert.DoesNotContain("DROP", bound.Sql);
        Assert.Equal(hostile, bound.Parameters[0]);
    }

    // ---- storing the definitions -------------------------------------------------------------------------------

    [Fact]
    public void Definitions_survive_being_stored_as_json()
    {
        var declared = new[] { new DatasetParameter("region", ParameterType.Text, J("\"Auckland\""), "Region"), Number("min", "10"), new DatasetParameter("since", ParameterType.Date) };

        var json = DatasetParametersJson.ToJson(declared);
        var read = DatasetParametersJson.FromJson(json);

        Assert.Contains("\"type\":\"Text\"", json); // readable, and stable if the enum is ever reordered
        Assert.Equal(["region", "min", "since"], read.Select(p => p.Name));
        Assert.Equal(ParameterType.Number, read[1].Type);
        Assert.Equal("Region", read[0].Label);
        Assert.Equal("Auckland", read[0].Default!.Value.GetString());
        Assert.Null(read[2].Default);
    }

    [Fact]
    public void No_parameters_is_stored_as_nothing()
    {
        Assert.Null(DatasetParametersJson.ToJson(null));
        Assert.Null(DatasetParametersJson.ToJson([]));
        Assert.Empty(DatasetParametersJson.FromJson(null));
        Assert.Empty(DatasetParametersJson.FromJson("  "));
    }
}
