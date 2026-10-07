using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>A real SQL Server all the way: connecting, the schema, SQL datasets (including ones with WITH), charts, table views and what is refused.</summary>
[Collection(ApiCollection.Name)]
public class MsSqlApiTests(ApiFactory factory)
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
            new { name = "Shop SQL Server", type = "SqlServer", connectionString = connection ?? factory.MsSqlConnectionString() }));
        return (client, org.Id, source.Id);
    }

    private static async Task<DatasetDto> DatasetAsync(HttpClient client, Guid org, Guid source, string sql = "SELECT * FROM dbo.orders", string name = "Orders") =>
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
            new { type = "SqlServer", connectionString = factory.MsSqlConnectionString() }));
        Assert.True(draft.Success, draft.Message);
        Assert.StartsWith("16.", draft.ServerVersion);

        var source = await ReadAsync<DataSourceDto>(await client.PostAsJsonAsync(Base(org.Id),
            new { name = "Shop", type = "SqlServer", connectionString = factory.MsSqlConnectionString() }));
        Assert.Equal("SqlServer", source.Type);
        Assert.True((await ReadAsync<TestResult>(await client.PostAsync($"{Base(org.Id)}/{source.Id}/test", null))).Success);
    }

    [Fact]
    public async Task A_wrong_password_is_reported_in_plain_words_without_the_server_details()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var result = await ReadAsync<TestResult>(await client.PostAsJsonAsync(Base(org.Id) + "/test",
            new { type = "SqlServer", connectionString = factory.MsSqlConnectionString(password: "Wrong-pw-123!") }));

        Assert.False(result.Success);
        Assert.Equal("Authentication failed. Check the username and password.", result.Message);
    }

    [Fact]
    public async Task A_database_that_is_not_there_is_reported()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var result = await ReadAsync<TestResult>(await client.PostAsJsonAsync(Base(org.Id) + "/test",
            new { type = "SqlServer", connectionString = factory.MsSqlConnectionString(database: "nothing_here") }));

        Assert.False(result.Success);
        Assert.Equal("The database does not exist, or the account may not open it.", result.Message);
    }

    [Fact]
    public async Task A_host_that_does_not_answer_is_reported_without_its_address()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var result = await ReadAsync<TestResult>(await client.PostAsJsonAsync(Base(org.Id) + "/test",
            new { type = "SqlServer", connectionString = factory.MsSqlConnectionString(port: 1) }));

        Assert.False(result.Success);
        Assert.DoesNotContain("127.0.0.1", result.Message);
    }

    [Theory]
    [InlineData("Integrated Security=True")]
    [InlineData("Authentication=Active Directory Default")]
    [InlineData("AttachDbFilename=C:\\secret.mdf")]
    [InlineData("Failover Partner=other")]
    [InlineData("Pooling=True")]
    public async Task Options_that_use_the_servers_identity_read_files_or_loosen_limits_are_refused(string option)
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var response = await client.PostAsJsonAsync(Base(org.Id),
            new { name = "Sneaky", type = "SqlServer", connectionString = factory.MsSqlConnectionString() + ";" + option });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ReadAsync<List<DataSourceDto>>(await client.GetAsync(Base(org.Id))));
    }

    [Fact]
    public async Task The_servers_own_databases_cannot_be_the_data_source()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var response = await client.PostAsJsonAsync(Base(org.Id),
            new { name = "Master", type = "SqlServer", connectionString = factory.MsSqlConnectionString(database: "master") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Viewers_cannot_add_a_data_source()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var (viewer, email, _) = await NewUserAsync(factory);
        (await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/members", new { email, role = "Viewer" })).EnsureSuccessStatusCode();

        var response = await viewer.PostAsJsonAsync(Base(org.Id), new { name = "x", type = "SqlServer", connectionString = factory.MsSqlConnectionString() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- schema and SQL -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_schema_shows_only_what_the_account_can_see_with_the_names_for_types_the_application_uses()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var schema = await ReadAsync<SchemaDto>(await client.GetAsync($"{Base(org)}/{source}/schema"));

        var node = Assert.Single(schema.Schemas);
        Assert.Equal("dbo", node.Name);
        var table = Assert.Single(node.Tables); // not secret_table: the reader may not select it
        Assert.Equal("orders", table.Name);
        var types = table.Columns.ToDictionary(c => c.Name, c => c.DataType);
        Assert.Equal("integer", types["id"]);
        Assert.Equal("text", types["region"]);
        Assert.Equal("numeric", types["price"]);
        Assert.Equal("date", types["ordered_on"]);
        Assert.Equal("timestamp without time zone", types["created_at"]);
        Assert.Equal("boolean", types["paid"]);
        Assert.False(table.Columns.Single(c => c.Name == "price").Nullable);
        Assert.True(table.Columns.Single(c => c.Name == "notes").Nullable);
    }

    [Fact]
    public async Task A_dataset_describes_its_columns_and_a_chart_query_answers_from_the_real_data()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var dataset = await DatasetAsync(client, org, source, "SELECT region, price, quantity, paid FROM dbo.orders");
        Assert.Equal(["region:text", "price:numeric", "quantity:integer", "paid:boolean"], dataset.Columns.Select(c => $"{c.Name}:{c.DataType}"));

        var result = await QueryAsync(client, org, dataset.Id, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new object[] { new { aggregation = "Sum", column = "price", alias = "revenue" } },
            orderBy = new[] { new { field = "revenue", direction = "Desc" } },
        });

        Assert.Equal(["Auckland", "Wellington", "Christchurch", "Dunedin"], Strings(result));
        Assert.Equal([2480m, 950m, 820m, 100m], Strings(result, 1).Select(decimal.Parse));
        Assert.Contains("[region]", result.Sql);
        Assert.Contains("TOP (101)", result.Sql);
    }

    [Fact]
    public async Task A_dataset_written_with_ctes_and_an_order_by_can_be_saved_and_previewed()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        const string sql = """
            WITH big AS (SELECT region, product, price FROM dbo.orders WHERE price >= 480),
                 named AS (SELECT region, product, price FROM big WHERE product IS NOT NULL)
            SELECT region, product, price FROM named ORDER BY region, price
            """;

        var dataset = await DatasetAsync(client, org, source, sql);
        Assert.Equal(["region", "product", "price"], dataset.Columns.Select(c => c.Name));

        var preview = await ReadAsync<QueryResult>(await client.PostAsJsonAsync($"{Base(org)}/{source}/preview", new { sql }));
        Assert.Equal(5, preview.Rows.Count); // 500, 1500, 520, 480 and the second 500; Dunedin has no product
        Assert.Equal(["Auckland", "Auckland", "Auckland", "Christchurch", "Wellington"], Strings(preview));
    }

    [Fact]
    public async Task Charts_on_a_cte_dataset_group_and_limit_series()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var dataset = await DatasetAsync(client, org, source,
            "WITH o AS (SELECT region, product, price, ordered_on FROM dbo.orders) SELECT region, product, price, ordered_on FROM o ORDER BY region");

        var grouped = await QueryAsync(client, org, dataset.Id, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
            orderBy = new[] { new { field = "n", direction = "Desc" } },
        });
        Assert.Equal("3", Strings(grouped, 1)[0]);

        var series = await QueryAsync(client, org, dataset.Id, new
        {
            dimensions = new object[] { new { column = "ordered_on", grain = "Year", alias = "year" }, new { column = "product" } },
            measures = new object[] { new { aggregation = "Sum", column = "price", alias = "revenue" } },
            orderBy = new[] { new { field = "year", direction = "Asc" } },
            seriesLimit = 2,
        });
        Assert.Equal(["Laptop", "Phone"], Strings(series, 1).Order());
    }

    [Fact]
    public async Task Groups_by_month_quarter_and_week_like_the_other_databases_do()
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
        Assert.Equal("1", await Count(new { column = "created_at", @operator = "Gt", value = "2026-03-31T00:00:00Z" }));
    }

    [Fact]
    public async Task A_percent_underscore_bracket_or_backslash_in_a_search_is_only_that_character()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var dataset = await DatasetAsync(client, org, source);

        async Task<string> Count(string text) => Strings(await QueryAsync(client, org, dataset.Id, new
        {
            measures = new[] { new { aggregation = "Count", alias = "n" } },
            filters = new[] { new { column = "notes", @operator = "Contains", value = text } },
        }))[0];

        Assert.Equal("1", await Count("50%_off"));
        Assert.Equal("1", await Count("%"));       // only one note has a percent sign; a wildcard would match all that have a note
        Assert.Equal("0", await Count("5_"));      // "50" would match if the underscore were a wildcard
        Assert.Equal("1", await Count("\\ sale")); // a backslash is a backslash
        Assert.Equal("1", await Count("[tag]"));   // brackets are not a set of characters
        Assert.Equal("1", await Count("["));
    }

    [Fact]
    public async Task Averages_have_decimals_sums_do_not_overflow_and_distinct_counts_work()
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
        Assert.Equal(17d / 7, double.Parse(Strings(result, 2)[0]), 3); // 17 / 7 = 2.43, not 2

        var big = await DatasetAsync(client, org, source, "SELECT 2000000000 AS big UNION ALL SELECT 2000000000 UNION ALL SELECT 2000000000", "Big");
        var sum = await QueryAsync(client, org, big.Id, new { measures = new[] { new { aggregation = "Sum", column = "big", alias = "total" } } });
        Assert.Equal("6000000000", Strings(sum)[0]);
    }

    [Fact]
    public async Task A_dataset_can_take_parameters_like_on_other_databases()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/datasets", new
        {
            dataSourceId = source,
            name = "By region",
            sql = "SELECT region, price FROM dbo.orders WHERE region = {{region}}",
            parameters = new[] { new { name = "region", type = "Text", @default = "Auckland" } },
        });
        var dataset = await ReadAsync<DatasetDto>(response);

        var result = await QueryAsync(client, org, dataset.Id, new { measures = new[] { new { aggregation = "Count", alias = "n" } } });
        Assert.Equal("3", Strings(result)[0]);
    }

    // ---- what is refused ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("DELETE FROM dbo.orders")]
    [InlineData("DROP TABLE dbo.orders")]
    [InlineData("SELECT * INTO dbo.copy FROM dbo.orders")]
    [InlineData("EXEC xp_cmdshell 'dir'")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=x;', 'SELECT 1')")]
    [InlineData("SELECT * FROM sys.sql_logins")]
    [InlineData("SELECT * FROM master.dbo.sysdatabases")]
    [InlineData("SELECT * FROM (SELECT * FROM sys.sql_logins) t")]
    [InlineData("SELECT 1; DROP TABLE dbo.orders")]
    [InlineData("WAITFOR DELAY '00:00:30'")]
    public async Task Sql_that_writes_reaches_other_servers_or_waits_is_refused_and_the_data_stays(string sql)
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await client.PostAsJsonAsync($"{Base(org)}/{source}/preview", new { sql });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var still = await ReadAsync<QueryResult>(await client.PostAsJsonAsync($"{Base(org)}/{source}/preview", new { sql = "SELECT COUNT(*) AS n FROM dbo.orders" }));
        Assert.Equal("8", Strings(still)[0]);
    }

    [Fact]
    public async Task A_query_that_runs_too_long_is_stopped_with_a_message()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        const string sql = """
            WITH n AS (SELECT 1 AS x UNION ALL SELECT x + 1 FROM n WHERE x < 100)
            SELECT COUNT_BIG(*) AS c FROM n a, n b, n c, n d, n e, n f
            """;

        var response = await client.PostAsJsonAsync($"{Base(org)}/{source}/preview", new { sql });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("took too long", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_table_the_account_may_not_read_says_so_without_the_data()
    {
        var (client, org, source) = await OwnerWithSourceAsync();

        var response = await client.PostAsJsonAsync($"{Base(org)}/{source}/preview", new { sql = "SELECT * FROM dbo.secret_table" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("permission", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do-not-show", text);
    }

    // ---- table views -----------------------------------------------------------------------------------------

    private static object Join(string type) => new
    {
        dataSourceId = (object?)null,
        main = new { schema = "dbo", name = "orders", fields = new[] { "id", "region", "product" } },
        joins = new[]
        {
            new
            {
                type,
                table = new { schema = "dbo", name = "orders", fields = new[] { "region" } },
                on = new[] { new { leftStep = 0, leftField = "product", rightField = "product" } },
            },
        },
    };

    [Theory]
    [InlineData("Inner")]
    [InlineData("Left")]
    [InlineData("Right")]
    [InlineData("Full")]
    public async Task A_table_view_joins_tables_with_every_kind_of_join_and_is_written_with_brackets(string type)
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var shape = (dynamic)Join(type);

        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/datasets", new
        {
            dataSourceId = source,
            name = "Joined " + type,
            kind = "TableView",
            definition = new { main = shape.main, joins = shape.joins },
        });

        var dataset = await ReadAsync<DatasetDto>(response);
        Assert.Contains($"{type.ToUpperInvariant()} JOIN [dbo].[orders]", dataset.Sql);
        Assert.Equal(["id", "region", "product", "orders_region"], dataset.Columns.Select(c => c.Name));
    }
}
