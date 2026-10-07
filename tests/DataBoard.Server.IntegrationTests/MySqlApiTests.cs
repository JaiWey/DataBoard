using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>A real MySQL database all the way: connecting, the schema, SQL datasets, charts, table views and what is refused.</summary>
[Collection(ApiCollection.Name)]
public class MySqlApiTests(ApiFactory factory)
{
    private sealed record ColumnDto(string Name, string DataType, string? Role = null);
    private sealed record TestResult(bool Success, string Message, string? ServerVersion);
    private sealed record SchemaDto(List<SchemaNodeDto> Schemas);
    private sealed record SchemaNodeDto(string Name, List<TableDto> Tables);
    private sealed record TableDto(string Name, string Kind, List<SchemaColumnDto> Columns);
    private sealed record SchemaColumnDto(string Name, string DataType, bool Nullable);
    private sealed record DatasetDto(Guid Id, string Name, string? Sql, List<ColumnDto> Columns);
    private sealed record QueryResult(List<ColumnDto> Columns, List<List<JsonElement>> Rows, bool Truncated, string? Sql);

    private static string Base(Guid org) => $"/api/organizations/{org}/data-sources";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<(HttpClient Client, Guid Org, Guid Source)> OwnerWithSourceAsync(string? connection = null)
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var source = await ReadAsync<DataSourceDto>(await client.PostAsJsonAsync(Base(org.Id),
            new { name = "Shop MySQL", type = "MySql", connectionString = connection ?? factory.MySqlConnectionString() }));
        return (client, org.Id, source.Id);
    }

    private static async Task<DatasetDto> DatasetAsync(HttpClient client, Guid org, Guid source, string sql = "SELECT * FROM orders", string name = "Orders") =>
        await ReadAsync<DatasetDto>(await client.PostAsJsonAsync($"/api/organizations/{org}/datasets", new { dataSourceId = source, name, sql }));

    private static async Task<QueryResult> QueryAsync(HttpClient client, Guid org, Guid dataset, object query) =>
        await ReadAsync<QueryResult>(await client.PostAsJsonAsync($"/api/organizations/{org}/datasets/{dataset}/query", query));

    private static string[] Strings(QueryResult r, int column = 0) => r.Rows.Select(x => x[column].ValueKind == JsonValueKind.Null ? "∅" : x[column].ToString()).ToArray();

    // ---- connecting -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_connection_is_tested_before_and_after_it_is_saved()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var draft = await ReadAsync<TestResult>(await client.PostAsJsonAsync(Base(org.Id) + "/test",
            new { type = "MySql", connectionString = factory.MySqlConnectionString() }));
        Assert.True(draft.Success);
        Assert.StartsWith("8.", draft.ServerVersion);

        var source = await ReadAsync<DataSourceDto>(await client.PostAsJsonAsync(Base(org.Id),
            new { name = "Shop", type = "MySql", connectionString = factory.MySqlConnectionString() }));
        Assert.Equal("MySql", source.Type);
        Assert.True((await ReadAsync<TestResult>(await client.PostAsync($"{Base(org.Id)}/{source.Id}/test", null))).Success);
    }

    [Fact]
    public async Task A_wrong_password_is_reported_in_plain_words_without_the_server_details()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var result = await ReadAsync<TestResult>(await client.PostAsJsonAsync(Base(org.Id) + "/test",
            new { type = "MySql", connectionString = factory.MySqlConnectionString(password: "wrong") }));

        Assert.False(result.Success);
        Assert.Equal("Authentication failed. Check the username and password.", result.Message);
    }

    [Fact]
    public async Task A_database_that_is_not_there_is_reported()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var connection = factory.MySqlConnectionString().Replace("Database=test", "Database=nothing_here");

        var result = await ReadAsync<TestResult>(await client.PostAsJsonAsync(Base(org.Id) + "/test", new { type = "MySql", connectionString = connection }));

        Assert.False(result.Success);
        Assert.True(result.Message is "The database does not exist." or "Authentication failed. Check the username and password.", result.Message);
    }

    [Fact]
    public async Task A_host_that_does_not_answer_times_out_politely()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var result = await ReadAsync<TestResult>(await client.PostAsJsonAsync(Base(org.Id) + "/test",
            new { type = "MySql", connectionString = factory.MySqlConnectionString(port: 1) }));

        Assert.False(result.Success);
        Assert.DoesNotContain("127.0.0.1", result.Message);
    }

    [Theory]
    [InlineData("AllowLoadLocalInfile=true")]
    [InlineData("SslCert=/etc/passwd")]
    [InlineData("Allow User Variables=true")]
    [InlineData("Pooling=true")]
    public async Task Options_that_read_files_or_loosen_limits_are_refused(string option)
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var response = await client.PostAsJsonAsync(Base(org.Id),
            new { name = "Sneaky", type = "MySql", connectionString = factory.MySqlConnectionString() + ";" + option });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ReadAsync<List<DataSourceDto>>(await client.GetAsync(Base(org.Id))));
    }

    [Fact]
    public async Task The_servers_own_databases_cannot_be_the_data_source()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var response = await client.PostAsJsonAsync(Base(org.Id),
            new { name = "Accounts", type = "MySql", connectionString = factory.MySqlConnectionString().Replace("Database=test", "Database=mysql") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Viewers_cannot_add_a_data_source()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var (viewer, email, _) = await NewUserAsync(factory);
        (await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/members", new { email, role = "Viewer" })).EnsureSuccessStatusCode();

        var response = await viewer.PostAsJsonAsync(Base(org.Id), new { name = "x", type = "MySql", connectionString = factory.MySqlConnectionString() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- schema and SQL -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_schema_shows_only_what_the_account_can_see_with_the_names_for_types_the_application_uses()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var schema = await ReadAsync<SchemaDto>(await client.GetAsync($"{Base(org)}/{source}/schema"));

        var tables = schema.Schemas.SelectMany(s => s.Tables).ToList();
        Assert.Equal(["orders"], tables.Select(t => t.Name)); // not secret_table: the reader may not select it
        var types = tables[0].Columns.ToDictionary(c => c.Name, c => c.DataType);
        Assert.Equal("integer", types["id"]);
        Assert.Equal("text", types["region"]);
        Assert.Equal("numeric", types["price"]);
        Assert.Equal("date", types["ordered_on"]);
        Assert.Equal("timestamp without time zone", types["created_at"]);
        Assert.Equal("boolean", types["paid"]); // TINYINT(1)
        Assert.Equal("json", types["data"]);
        Assert.False(tables[0].Columns.Single(c => c.Name == "price").Nullable);
        Assert.True(tables[0].Columns.Single(c => c.Name == "notes").Nullable);
    }

    [Fact]
    public async Task A_dataset_describes_its_columns_and_a_chart_query_answers_from_the_real_data()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var dataset = await DatasetAsync(client, org, source, "SELECT region, price, quantity, paid FROM orders");
        Assert.Equal(["region:text", "price:numeric", "quantity:integer", "paid:boolean"], dataset.Columns.Select(c => $"{c.Name}:{c.DataType}"));

        var result = await QueryAsync(client, org, dataset.Id, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new object[] { new { aggregation = "Sum", column = "price", alias = "revenue" } },
            orderBy = new[] { new { field = "revenue", direction = "Desc" } },
        });

        Assert.Equal(["Auckland", "Wellington", "Christchurch", "Dunedin"], Strings(result));
        Assert.Equal([2480m, 950m, 820m, 100m], Strings(result, 1).Select(decimal.Parse));
        Assert.Contains("`region`", result.Sql);
    }

    [Fact]
    public async Task Groups_by_month_and_week_like_the_other_databases_do()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var dataset = await DatasetAsync(client, org, source);

        var months = await QueryAsync(client, org, dataset.Id, new
        {
            dimensions = new[] { new { column = "ordered_on", grain = "Month", alias = "month" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
            orderBy = new[] { new { field = "month", direction = "Asc" } },
        });
        Assert.Equal(["2026-01", "2026-02", "2026-03"], Strings(months));
        Assert.Equal(["2", "2", "4"], Strings(months, 1));

        var quarters = await QueryAsync(client, org, dataset.Id, new
        {
            dimensions = new[] { new { column = "created_at", grain = "Quarter" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
        });
        Assert.Equal(["2026-Q1"], Strings(quarters));

        var weeks = await QueryAsync(client, org, dataset.Id, new
        {
            dimensions = new[] { new { column = "ordered_on", grain = "Week" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
            filters = new[] { new { column = "ordered_on", @operator = "Gte", value = "2026-03-28" } },
        });
        Assert.Equal(["2026-03-23", "2026-03-30"], Strings(weeks).Select(s => s[..10]).Order()); // Mondays; 03-28 is a Saturday, 03-31 a Tuesday
    }

    [Fact]
    public async Task Filters_work_for_text_numbers_booleans_dates_and_missing_values()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var dataset = await DatasetAsync(client, org, source);

        async Task<string> Count(object filter) =>
            Strings(await QueryAsync(client, org, dataset.Id, new { measures = new[] { new { aggregation = "Count", alias = "n" } }, filters = new[] { filter } }))[0];

        Assert.Equal("3", await Count(new { column = "region", @operator = "Eq", value = "Auckland" }));
        Assert.Equal("4", await Count(new { column = "region", @operator = "In", values = new[] { "Auckland", "Dunedin" } }));
        Assert.Equal("4", await Count(new { column = "price", @operator = "Between", values = new[] { 450, 500 } }));
        Assert.Equal("6", await Count(new { column = "paid", @operator = "Eq", value = true }));
        Assert.Equal("4", await Count(new { column = "ordered_on", @operator = "Gte", value = "2026-03-01" }));
        Assert.Equal("1", await Count(new { column = "quantity", @operator = "IsNull" }));
        Assert.Equal("7", await Count(new { column = "quantity", @operator = "IsNotNull" }));
        Assert.Equal("2", await Count(new { column = "notes", @operator = "Contains", value = "RUSH" })); // not case sensitive
        Assert.Equal("2", await Count(new { column = "region", @operator = "StartsWith", value = "well" }));
    }

    [Fact]
    public async Task A_percent_or_underscore_or_backslash_in_a_search_is_only_that_character()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var dataset = await DatasetAsync(client, org, source);

        async Task<string> Count(string text) => Strings(await QueryAsync(client, org, dataset.Id, new
        {
            measures = new[] { new { aggregation = "Count", alias = "n" } },
            filters = new[] { new { column = "notes", @operator = "Contains", value = text } },
        }))[0];

        Assert.Equal("1", await Count("50%_off"));
        Assert.Equal("1", await Count("%"));      // only the one note has a percent sign; a wildcard would match all seven
        Assert.Equal("0", await Count("5_"));     // "50" would match if the underscore were a wildcard
        Assert.Equal("1", await Count("\\ sale")); // a backslash is a backslash
    }

    [Fact]
    public async Task A_series_limit_keeps_the_top_series_and_counts_a_missing_one_as_a_series()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var dataset = await DatasetAsync(client, org, source);

        var result = await QueryAsync(client, org, dataset.Id, new
        {
            dimensions = new object[] { new { column = "ordered_on", grain = "Year", alias = "year" }, new { column = "product" } },
            measures = new object[] { new { aggregation = "Sum", column = "price", alias = "revenue" } },
            orderBy = new[] { new { field = "year", direction = "Asc" } },
            seriesLimit = 2,
        });

        Assert.Equal(["Laptop", "Phone"], Strings(result, 1).Order()); // the two largest totals: Phone 2450, Laptop 1500; not Tablet 300
    }

    [Fact]
    public async Task Averages_distinct_counts_and_highest_values_come_out_right()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var dataset = await DatasetAsync(client, org, source);

        var result = await QueryAsync(client, org, dataset.Id, new
        {
            measures = new object[]
            {
                new { aggregation = "CountDistinct", column = "region", alias = "regions" },
                new { aggregation = "Max", column = "price", alias = "top" },
                new { aggregation = "Avg", column = "quantity", alias = "mean" },
            },
        });

        Assert.Equal("4", Strings(result)[0]);
        Assert.Equal(1500m, decimal.Parse(Strings(result, 1)[0]));
        Assert.Equal(17d / 7, double.Parse(Strings(result, 2)[0]), 3);
    }

    [Fact]
    public async Task A_dataset_can_take_parameters_like_on_other_databases()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/datasets", new
        {
            dataSourceId = source,
            name = "By region",
            sql = "SELECT region, price FROM orders WHERE region = {{region}}",
            parameters = new[] { new { name = "region", type = "Text", @default = "Auckland" } },
        });
        var dataset = await ReadAsync<DatasetDto>(response);

        var result = await QueryAsync(client, org, dataset.Id, new { measures = new[] { new { aggregation = "Count", alias = "n" } } });
        Assert.Equal("3", Strings(result)[0]);
    }

    // ---- what is refused ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("DELETE FROM orders")]
    [InlineData("DROP TABLE orders")]
    [InlineData("SELECT * FROM orders INTO OUTFILE '/tmp/x'")]
    [InlineData("SELECT LOAD_FILE('/etc/passwd')")]
    [InlineData("SELECT SLEEP(30)")]
    [InlineData("SELECT * FROM mysql.user")]
    [InlineData("SELECT * FROM (SELECT LOAD_FILE('/etc/passwd')) t")]
    [InlineData("SELECT 1; DROP TABLE orders")]
    public async Task Sql_that_writes_reads_files_or_waits_is_refused_and_the_data_stays(string sql)
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await client.PostAsJsonAsync($"{Base(org)}/{source}/preview", new { sql });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var still = await ReadAsync<QueryResult>(await client.PostAsJsonAsync($"{Base(org)}/{source}/preview", new { sql = "SELECT COUNT(*) AS n FROM orders" }));
        Assert.Equal("8", Strings(still)[0]);
    }

    [Fact]
    public async Task A_function_that_writes_is_stopped_by_the_read_only_transaction_even_for_an_account_that_could_write()
    {
        // Root can write. The SQL passes the validator (it is only a function call); the database must stop the write.
        await factory.ExecuteOnMySqlAsync("""
            SET GLOBAL log_bin_trust_function_creators = 1;
            CREATE TABLE IF NOT EXISTS audit (n INT);
            DROP FUNCTION IF EXISTS do_write;
            CREATE FUNCTION do_write() RETURNS INT MODIFIES SQL DATA BEGIN INSERT INTO audit VALUES (1); RETURN 1; END;
            """);
        var (client, org, source) = await OwnerWithSourceAsync(factory.MySqlConnectionString("root", ApiFactory.MySqlRootPassword));

        var response = await client.PostAsJsonAsync($"{Base(org)}/{source}/preview", new { sql = "SELECT do_write() AS x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("READ ONLY", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_query_that_runs_too_long_is_stopped_with_a_message()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        const string sql = """
            WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 30000000)
            SELECT COUNT(*) AS n FROM c a JOIN c b ON a.x = b.x + 1 WHERE a.x % 7 = 3
            """;

        var response = await client.PostAsJsonAsync($"{Base(org)}/{source}/preview", new { sql });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(text.Contains("maximum statement execution time", StringComparison.OrdinalIgnoreCase)
            || text.Contains("interrupted", StringComparison.OrdinalIgnoreCase)
            || text.Contains("max_recursion", StringComparison.OrdinalIgnoreCase)
            || text.Contains("recursion", StringComparison.OrdinalIgnoreCase), text);
    }

    [Fact]
    public async Task A_table_the_account_may_not_read_says_so_without_the_server_details()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await client.PostAsJsonAsync($"{Base(org)}/{source}/preview", new { sql = "SELECT * FROM secret_table" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("do-not-show", await response.Content.ReadAsStringAsync());
    }

    // ---- table views -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_table_view_joins_tables_and_is_written_with_backticks()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/datasets", new
        {
            dataSourceId = source,
            name = "Orders with the same product",
            kind = "TableView",
            definition = new
            {
                main = new { schema = "test", name = "orders", fields = new[] { "id", "region", "product" } },
                joins = new[]
                {
                    new
                    {
                        type = "Inner",
                        table = new { schema = "test", name = "orders", fields = new[] { "region" } },
                        on = new[] { new { leftStep = 0, leftField = "product", rightField = "product" } },
                    },
                },
            },
        });

        var dataset = await ReadAsync<DatasetDto>(response);
        Assert.Contains("INNER JOIN `test`.`orders`", dataset.Sql);
        Assert.Equal(["id", "region", "product", "orders_region"], dataset.Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task A_full_join_is_refused_in_words_because_mysql_has_none()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/datasets", new
        {
            dataSourceId = source,
            name = "Full",
            kind = "TableView",
            definition = new
            {
                main = new { schema = "test", name = "orders", fields = new[] { "id" } },
                joins = new[]
                {
                    new
                    {
                        type = "Full",
                        table = new { schema = "test", name = "orders", fields = new[] { "region" } },
                        on = new[] { new { leftStep = 0, leftField = "id", rightField = "id" } },
                    },
                },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no full outer join", await response.Content.ReadAsStringAsync());
    }
}
