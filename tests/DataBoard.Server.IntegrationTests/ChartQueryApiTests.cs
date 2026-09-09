using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>
/// Runs chart queries against the fixed "orders" table (8 rows, see ApiFactory) and checks the answers by hand:
/// Auckland 3 orders, Wellington 2, Christchurch 2, Dunedin 1.
/// </summary>
[Collection(ApiCollection.Name)]
public class ChartQueryApiTests(ApiFactory factory)
{
    private sealed record ColumnDto(string Name, string DataType);
    private sealed record ChartResult(List<ColumnDto> Columns, List<List<JsonElement>> Rows, bool Truncated, string? Sql);

    private const string OrdersSql = "SELECT id, region, product, quantity, price, ordered_on, created_at, paid, customer, notes FROM orders";

    private sealed record Fixture(HttpClient Owner, Guid Org, Guid Source, Guid Dataset);

    private async Task<Fixture> NewFixtureAsync(string datasetSql = OrdersSql)
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var sourceResponse = await client.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        sourceResponse.EnsureSuccessStatusCode();
        var source = (await sourceResponse.Content.ReadFromJsonAsync<DataSourceDto>(Json))!;

        var dataset = await CreateDatasetAsync(client, org.Id, source.Id, "Orders", datasetSql);
        return new Fixture(client, org.Id, source.Id, dataset);
    }

    private static async Task<Guid> CreateDatasetAsync(HttpClient client, Guid org, Guid source, string name, string sql)
    {
        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/datasets",
            new { dataSourceId = source, name, sql });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> QueryAsync(HttpClient client, Fixture f, object query) =>
        client.PostAsJsonAsync($"/api/organizations/{f.Org}/datasets/{f.Dataset}/query", query);

    private static async Task<ChartResult> RunAsync(HttpClient client, Fixture f, object query)
    {
        var response = await QueryAsync(client, f, query);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ChartResult>(Json))!;
    }

    private static async Task<long> CountAsync(HttpClient client, Fixture f, params object[] filters)
    {
        var result = await RunAsync(client, f, new { measures = new[] { new { aggregation = "Count" } }, filters });
        return result.Rows.Single()[0].GetInt64();
    }

    private static string Str(JsonElement e) => e.ValueKind == JsonValueKind.Null ? "∅" : e.ToString();

    // ---- grouping and measures -------------------------------------------------------------------------------

    [Fact]
    public async Task Group_by_region_with_sum_count_and_average()
    {
        var f = await NewFixtureAsync();

        var result = await RunAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new object[]
            {
                new { aggregation = "Count", alias = "orders" },
                new { aggregation = "Sum", column = "price", alias = "revenue" },
                new { aggregation = "Sum", column = "quantity", alias = "units" },
                new { aggregation = "Avg", column = "price", alias = "avg_price" },
            },
        });

        Assert.Equal(["region", "orders", "revenue", "units", "avg_price"], result.Columns.Select(c => c.Name));
        // Sorted by the dimension by default.
        Assert.Equal(["Auckland", "Christchurch", "Dunedin", "Wellington"], result.Rows.Select(r => r[0].GetString()));

        var auckland = result.Rows[0];
        Assert.Equal(3, auckland[1].GetInt64());
        Assert.Equal(2480m, auckland[2].GetDecimal());
        Assert.Equal(4, auckland[3].GetInt64());

        var dunedin = result.Rows[2];
        Assert.Equal(1, dunedin[1].GetInt64());
        Assert.Equal(JsonValueKind.Null, dunedin[3].ValueKind); // SUM of only NULLs

        Assert.Equal(475m, result.Rows[3][4].GetDecimal()); // Wellington: (500 + 450) / 2
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Measures_only_returns_one_row_and_count_distinct_ignores_nulls()
    {
        var f = await NewFixtureAsync();

        var result = await RunAsync(f.Owner, f, new
        {
            measures = new object[]
            {
                new { aggregation = "Count", alias = "n" },
                new { aggregation = "CountDistinct", column = "product", alias = "products" },
                new { aggregation = "Min", column = "price", alias = "cheapest" },
                new { aggregation = "Max", column = "ordered_on", alias = "latest" },
            },
        });

        var row = Assert.Single(result.Rows);
        Assert.Equal(8, row[0].GetInt64());
        Assert.Equal(3, row[1].GetInt64()); // Phone, Laptop, Tablet
        Assert.Equal(100m, row[2].GetDecimal());
        Assert.StartsWith("2026-03-31", row[3].GetString());
    }

    [Fact]
    public async Task Dimensions_without_measures_return_the_distinct_combinations()
    {
        var f = await NewFixtureAsync();

        var result = await RunAsync(f.Owner, f, new { dimensions = new[] { new { column = "region" } } });

        Assert.Equal(4, result.Rows.Count);
    }

    [Fact]
    public async Task Time_grain_buckets_dates_by_month()
    {
        var f = await NewFixtureAsync();

        var result = await RunAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "ordered_on", grain = "Month" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" }, new { aggregation = "Sum", column = "price", alias = "revenue" } },
        });

        Assert.Equal(["ordered_on_month", "n", "revenue"], result.Columns.Select(c => c.Name));
        Assert.Equal(
            [("2026-01-01", 2L, 2000m), ("2026-02-01", 2L, 950m), ("2026-03-01", 4L, 1400m)],
            result.Rows.Select(r => (r[0].GetString()![..10], r[1].GetInt64(), r[2].GetDecimal())));
    }

    [Fact]
    public async Task Timestamp_buckets_are_in_utc_so_a_late_evening_order_stays_on_its_day()
    {
        // 2026-02-17T23:30Z would fall on the 18th in New Zealand time; the session is pinned to UTC.
        var f = await NewFixtureAsync();

        var result = await RunAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "created_at", grain = "Day" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
            filters = new[] { new { column = "region", @operator = "Eq", value = "Wellington" } },
        });

        Assert.Equal(["2026-02-03", "2026-02-17"], result.Rows.Select(r => r[0].GetString()![..10]));
    }

    [Theory]
    [InlineData("Day", 8)]     // all eight order dates are different days...
    [InlineData("Week", 8)]    // ...and fall in eight different weeks
    [InlineData("Quarter", 1)]
    [InlineData("Year", 1)]
    public async Task Every_grain_runs(string grain, int expectedBuckets)
    {
        var f = await NewFixtureAsync();

        var result = await RunAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "ordered_on", grain } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
        });

        Assert.Equal(expectedBuckets, result.Rows.Count);
        Assert.Equal(8, result.Rows.Sum(r => r[1].GetInt64()));
    }

    // ---- filters ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Comparison_filters()
    {
        var f = await NewFixtureAsync();

        Assert.Equal(6, await CountAsync(f.Owner, f, new { column = "paid", @operator = "Eq", value = true }));
        Assert.Equal(3, await CountAsync(f.Owner, f, new { column = "quantity", @operator = "Gte", value = 3 }));
        Assert.Equal(4, await CountAsync(f.Owner, f, new { column = "ordered_on", @operator = "Gte", value = "2026-03-01" }));
        Assert.Equal(3, await CountAsync(f.Owner, f, new { column = "customer", @operator = "Eq", value = "11111111-1111-1111-1111-111111111111" }));
        Assert.Equal(7, await CountAsync(f.Owner, f, new { column = "price", @operator = "NotEq", value = 100 }));
        Assert.Equal(1, await CountAsync(f.Owner, f, new { column = "created_at", @operator = "Gt", value = "2026-03-31T00:00:00Z" }));
    }

    [Fact]
    public async Task List_range_and_null_filters()
    {
        var f = await NewFixtureAsync();

        Assert.Equal(5, await CountAsync(f.Owner, f, new { column = "region", @operator = "In", values = new[] { "Auckland", "Wellington" } }));
        Assert.Equal(3, await CountAsync(f.Owner, f, new { column = "region", @operator = "NotIn", values = new[] { "Auckland", "Wellington" } }));
        Assert.Equal(4, await CountAsync(f.Owner, f, new { column = "price", @operator = "Between", values = new[] { 450, 500 } }));
        Assert.Equal(2, await CountAsync(f.Owner, f, new { column = "customer", @operator = "IsNull" }));
        Assert.Equal(1, await CountAsync(f.Owner, f, new { column = "product", @operator = "IsNull" }));
        Assert.Equal(3, await CountAsync(f.Owner, f, new { column = "notes", @operator = "IsNotNull" }));
    }

    [Fact]
    public async Task Filters_combine_with_and()
    {
        var f = await NewFixtureAsync();

        var count = await CountAsync(f.Owner, f,
            new { column = "region", @operator = "Eq", value = "Auckland" },
            new { column = "paid", @operator = "Eq", value = true });

        Assert.Equal(2, count); // rows 1 and 7
    }

    [Fact]
    public async Task Text_search_is_case_insensitive_and_wildcards_match_literally()
    {
        var f = await NewFixtureAsync();

        Assert.Equal(2, await CountAsync(f.Owner, f, new { column = "notes", @operator = "Contains", value = "RUSH" }));
        Assert.Equal(2, await CountAsync(f.Owner, f, new { column = "notes", @operator = "StartsWith", value = "rush" }));
        // A bare % must not act as "match everything": only the note that really contains a percent sign.
        Assert.Equal(1, await CountAsync(f.Owner, f, new { column = "notes", @operator = "Contains", value = "%" }));
        Assert.Equal(1, await CountAsync(f.Owner, f, new { column = "notes", @operator = "Contains", value = "_" }));
        Assert.Equal(1, await CountAsync(f.Owner, f, new { column = "notes", @operator = "Contains", value = "\\" }));
        Assert.Equal(1, await CountAsync(f.Owner, f, new { column = "notes", @operator = "StartsWith", value = "50%_off\\" }));
    }

    [Fact]
    public async Task Measure_filters_keep_only_groups_that_pass_and_order_can_use_a_measure()
    {
        var f = await NewFixtureAsync();

        var result = await RunAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new object[] { new { aggregation = "Sum", column = "price", alias = "revenue" } },
            measureFilters = new[] { new { column = "revenue", @operator = "Gt", value = 900 } },
            orderBy = new[] { new { field = "revenue", direction = "Desc" } },
        });

        Assert.Equal(["Auckland", "Wellington"], result.Rows.Select(r => r[0].GetString()));
        Assert.Equal([2480m, 950m], result.Rows.Select(r => r[1].GetDecimal()));
    }

    [Fact]
    public async Task Filters_apply_before_grouping()
    {
        var f = await NewFixtureAsync();

        var result = await RunAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
            filters = new[] { new { column = "paid", @operator = "Eq", value = false } },
        });

        Assert.Equal([("Auckland", 1L), ("Christchurch", 1L)], result.Rows.Select(r => (r[0].GetString()!, r[1].GetInt64())));
    }

    // ---- limit and truncation --------------------------------------------------------------------------------

    [Fact]
    public async Task Limit_cuts_the_result_and_says_so()
    {
        var f = await NewFixtureAsync();

        var cut = await RunAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
            limit = 2,
        });
        var exact = await RunAsync(f.Owner, f, new { dimensions = new[] { new { column = "region" } }, limit = 4 });

        Assert.Equal(["Auckland", "Christchurch"], cut.Rows.Select(r => r[0].GetString()));
        Assert.True(cut.Truncated);
        Assert.Equal(4, exact.Rows.Count);
        Assert.False(exact.Truncated);
    }

    [Fact]
    public async Task Descending_order_puts_the_biggest_group_first()
    {
        var f = await NewFixtureAsync();

        var result = await RunAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
            orderBy = new[] { new { field = "n", direction = "Desc" } },
            limit = 1,
        });

        Assert.Equal("Auckland", Assert.Single(result.Rows)[0].GetString());
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task A_series_limit_keeps_the_biggest_series_and_breaks_ties_by_name()
    {
        var f = await NewFixtureAsync();
        object Query(int seriesLimit) => new
        {
            dimensions = new[] { new { column = "product" }, new { column = "region" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
            seriesLimit,
        };

        var top2 = await RunAsync(f.Owner, f, Query(2));
        var all = await RunAsync(f.Owner, f, Query(10));

        // Auckland has 3 orders; Wellington and Christchurch tie on 2, and Christchurch comes first alphabetically.
        Assert.Equal(["Auckland", "Christchurch"], top2.Rows.Select(r => r[1].GetString()).Distinct().Order());
        Assert.Equal(["Auckland", "Christchurch", "Dunedin", "Wellington"], all.Rows.Select(r => r[1].GetString()).Distinct().Order());
        Assert.Equal(5, top2.Rows.Sum(r => r[2].GetInt64()));
    }

    [Fact]
    public async Task A_series_limit_without_a_second_dimension_is_rejected()
    {
        var f = await NewFixtureAsync();

        var response = await QueryAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new object[] { new { aggregation = "Count" } },
            seriesLimit = 2,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- awkward datasets ------------------------------------------------------------------------------------

    [Fact]
    public async Task Columns_called_a_dot_b_or_x_as_y_or_with_quotes_and_question_marks_work()
    {
        var f = await NewFixtureAsync("SELECT 1 AS \"a.b\", 2 AS \"x as y\", 3 AS \"q?\", 'k' AS \"we\"\"ird\"");

        var result = await RunAsync(f.Owner, f, new
        {
            dimensions = new object[] { new { column = "a.b", alias = "dotted" }, new { column = "we\"ird", alias = "quoted" } },
            measures = new object[] { new { aggregation = "Sum", column = "x as y", alias = "total" } },
            filters = new[] { new { column = "q?", @operator = "Eq", value = 3 } },
        });

        var row = Assert.Single(result.Rows);
        Assert.Equal(["dotted", "quoted", "total"], result.Columns.Select(c => c.Name));
        Assert.Equal((1, "k", 2), (row[0].GetInt32(), row[1].GetString(), row[2].GetInt32()));
    }

    [Fact]
    public async Task Dataset_sql_with_json_operators_a_trailing_comment_and_array_slices_runs_untouched()
    {
        var f = await NewFixtureAsync(
            "SELECT id, data ? 'k' AS has_k, data @> '{\"a\":1}'::jsonb AS has_a, (ARRAY[1,2,3])[1:2] AS slice FROM orders -- trailing comment");

        var result = await RunAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "has_k" }, new { column = "has_a" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
        });

        // Row 1 has {"k":1}, row 2 has {"a":1}, the other six have no JSON at all (NULL).
        var groups = result.Rows.ToDictionary(r => $"{Str(r[0])}/{Str(r[1])}", r => r[2].GetInt64());
        Assert.Equal(
            new Dictionary<string, long> { ["True/False"] = 1, ["False/True"] = 1, ["∅/∅"] = 6 },
            groups);
    }

    [Fact]
    public async Task Arrays_can_be_grouped_but_not_filtered_or_summed()
    {
        var f = await NewFixtureAsync("SELECT id, (ARRAY[1,2,3])[1:2] AS slice FROM orders");

        var grouped = await RunAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "slice" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
        });
        Assert.Equal(8, Assert.Single(grouped.Rows)[1].GetInt64());

        Assert.Equal(HttpStatusCode.BadRequest, (await QueryAsync(f.Owner, f, new
        {
            measures = new[] { new { aggregation = "Sum", column = "slice" } },
        })).StatusCode);
    }

    // ---- hostile input ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("' OR '1'='1")]
    [InlineData("'; DROP TABLE orders; --")]
    [InlineData("\" OR 1=1 --")]
    [InlineData("%")]
    public async Task Hostile_filter_values_are_just_text_and_match_nothing(string attack)
    {
        var f = await NewFixtureAsync();

        var count = await CountAsync(f.Owner, f, new { column = "region", @operator = "Eq", value = attack });

        Assert.Equal(0, count);
        Assert.Equal(8, await CountAsync(f.Owner, f)); // the table is intact
    }

    [Theory]
    [InlineData("n\" FROM orders; DROP TABLE orders; --")]
    [InlineData("a b")]
    [InlineData("1x")]
    [InlineData("x;y")]
    public async Task Hostile_aliases_are_rejected(string alias)
    {
        var f = await NewFixtureAsync();

        var response = await QueryAsync(f.Owner, f, new { measures = new[] { new { aggregation = "Count", alias } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("region\" , (SELECT token FROM secret_table LIMIT 1) AS \"x")]
    [InlineData("REGION")]
    public async Task Unknown_columns_are_rejected_in_dimensions_measures_and_filters(string column)
    {
        var f = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await QueryAsync(f.Owner, f, new { dimensions = new[] { new { column } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QueryAsync(f.Owner, f, new { measures = new[] { new { aggregation = "Count", column } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QueryAsync(f.Owner, f, new
        {
            measures = new[] { new { aggregation = "Count" } },
            filters = new[] { new { column, @operator = "IsNull" } },
        })).StatusCode);
    }

    [Fact]
    public async Task A_dataset_can_only_expose_its_own_columns_not_the_rest_of_the_table()
    {
        // The dataset selects only id and region, so "price" must not be reachable even though the table has it.
        var f = await NewFixtureAsync("SELECT id, region FROM orders");

        var response = await QueryAsync(f.Owner, f, new { measures = new[] { new { aggregation = "Sum", column = "price" } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not a column of this dataset", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_queries_report_every_problem_with_its_location()
    {
        var f = await NewFixtureAsync();

        var response = await QueryAsync(f.Owner, f, new
        {
            dimensions = new[] { new { column = "nope" } },
            measures = new[] { new { aggregation = "Sum", column = "region" } },
            limit = 0,
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Dimensions[0]", body);
        Assert.Contains("Measures[0]", body);
        Assert.Contains("Limit", body);
    }

    [Fact]
    public async Task An_empty_or_missing_body_is_a_400_not_a_crash()
    {
        var f = await NewFixtureAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await QueryAsync(f.Owner, f, new { })).StatusCode);
        var noBody = await f.Owner.PostAsync($"/api/organizations/{f.Org}/datasets/{f.Dataset}/query", null);
        Assert.True((int)noBody.StatusCode is 400 or 415);
    }

    [Fact]
    public async Task Tampered_dataset_sql_is_refused_before_any_chart_query_runs()
    {
        var f = await NewFixtureAsync();
        var before = await factory.CountSalesAsync();

        using (var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(factory.Services))
        {
            var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<DataBoard.Infrastructure.Persistence.AppDbContext>(scope.ServiceProvider);
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(
                db.Database, "UPDATE datasets SET \"Sql\" = 'SELECT 1; DELETE FROM sales' WHERE \"Id\" = {0}", f.Dataset);
        }

        var response = await QueryAsync(f.Owner, f, new { measures = new[] { new { aggregation = "Count" } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await factory.CountSalesAsync());
    }

    // ---- who may run what ------------------------------------------------------------------------------------

    [Fact]
    public async Task Viewers_can_run_queries_but_never_receive_the_sql_and_editors_do()
    {
        var f = await NewFixtureAsync();
        var (viewer, viewerEmail, _) = await NewUserAsync(factory);
        await f.Owner.PostAsJsonAsync($"/api/organizations/{f.Org}/members", new { email = viewerEmail, role = "Viewer" });
        var query = new { measures = new[] { new { aggregation = "Count" } } };

        var asViewer = await RunAsync(viewer, f, query);
        var asOwner = await RunAsync(f.Owner, f, query);

        Assert.Equal(8, asViewer.Rows.Single()[0].GetInt64());
        Assert.Null(asViewer.Sql);
        Assert.NotNull(asOwner.Sql);
        Assert.Contains("\"databoard_dataset\"", asOwner.Sql);
        Assert.Contains("FROM orders", asOwner.Sql);
    }

    [Fact]
    public async Task Strangers_and_anonymous_callers_cannot_query()
    {
        var f = await NewFixtureAsync();
        var (stranger, _, _) = await NewUserAsync(factory);
        var strangersOrg = await CreateOrgAsync(stranger, "Mine");
        var query = new { measures = new[] { new { aggregation = "Count" } } };

        Assert.Equal(HttpStatusCode.NotFound, (await QueryAsync(stranger, f, query)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PostAsJsonAsync($"/api/organizations/{strangersOrg.Id}/datasets/{f.Dataset}/query", query)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await QueryAsync(factory.CreateApiClient(), f, query)).StatusCode);
    }

    [Fact]
    public async Task Database_privileges_still_apply_so_a_dataset_cannot_reach_a_table_the_account_cannot_read()
    {
        // Creating the dataset itself already fails, which is the first line of defence.
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var source = (await (await client.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() })).Content.ReadFromJsonAsync<DataSourceDto>(Json))!;

        var response = await client.PostAsJsonAsync($"/api/organizations/{org.Id}/datasets",
            new { dataSourceId = source.Id, name = "Secrets", sql = "SELECT token FROM secret_table" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("permission denied", await response.Content.ReadAsStringAsync());
    }
}
