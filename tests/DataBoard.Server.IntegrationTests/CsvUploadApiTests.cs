using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>An uploaded CSV file all the way: upload, query, chart, replace the file, delete, and who may do which.</summary>
[Collection(ApiCollection.Name)]
public class CsvUploadApiTests(ApiFactory factory)
{
    private sealed record ColumnDto(string Name, string DataType);
    private sealed record UploadResult(DataSourceDto DataSource, string TableName, int RowCount, List<ColumnDto> Columns);
    private sealed record DatasetDto(Guid Id, string Name, List<ColumnDto> Columns);
    private sealed record QueryResult(List<ColumnDto> Columns, List<List<JsonElement>> Rows, bool Truncated, string? Sql);
    private sealed record SchemaDto(List<SchemaNodeDto> Schemas);
    private sealed record SchemaNodeDto(string Name, List<TableDto> Tables);
    private sealed record TableDto(string Name, List<ColumnDto> Columns);

    private const string Sales = "region,amount,sold_on\nNorth,10,2026-01-05\nNorth,20,2026-01-19\nSouth,5,2026-02-02\n";

    private static string Base(Guid org) => $"/api/organizations/{org}/data-sources";

    private static MultipartFormDataContent Form(string csv, string fileName = "sales.csv", string? name = "Shop CSV", params (string Key, string Value)[] extra)
    {
        var form = new MultipartFormDataContent();
        if (name is not null) form.Add(new StringContent(name), "Name");
        foreach (var (key, value) in extra) form.Add(new StringContent(value), key);
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "File", fileName);
        return form;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<(HttpClient Client, Guid Org, UploadResult Upload)> OwnerWithUploadAsync(string csv = Sales)
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var upload = await ReadAsync<UploadResult>(await client.PostAsync(Base(org.Id) + "/csv", Form(csv)));
        return (client, org.Id, upload);
    }

    private async Task<HttpClient> AddMemberAsync(HttpClient owner, Guid org, string role)
    {
        var (member, email, _) = await NewUserAsync(factory);
        (await owner.PostAsJsonAsync($"/api/organizations/{org}/members", new { email, role })).EnsureSuccessStatusCode();
        return member;
    }

    private static async Task<DatasetDto> CreateDatasetAsync(HttpClient client, Guid org, Guid source, string sql, string name = "Sales")
    {
        return await ReadAsync<DatasetDto>(await client.PostAsJsonAsync($"/api/organizations/{org}/datasets", new { dataSourceId = source, name, sql }));
    }

    private static Task<HttpResponseMessage> QueryAsync(HttpClient client, Guid org, Guid dataset, object query) =>
        client.PostAsJsonAsync($"/api/organizations/{org}/datasets/{dataset}/query", query);

    private int FilesOnDisk() => Directory.Exists(factory.UploadFolder) ? Directory.GetFiles(factory.UploadFolder, "*.db").Length : 0;

    // ---- uploading ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Uploading_a_file_makes_a_data_source_and_says_what_was_read()
    {
        var (client, org, upload) = await OwnerWithUploadAsync();

        Assert.Equal("Csv", upload.DataSource.Type);
        Assert.Equal("Shop CSV", upload.DataSource.Name);
        Assert.Equal("sales", upload.TableName);
        Assert.Equal(3, upload.RowCount);
        Assert.Equal(["region:text", "amount:bigint", "sold_on:date"], upload.Columns.Select(c => $"{c.Name}:{c.DataType}"));

        var listed = await ReadAsync<List<DataSourceDto>>(await client.GetAsync(Base(org)));
        Assert.Contains(listed, d => d.Id == upload.DataSource.Id && d.Type == "Csv");
    }

    [Fact]
    public async Task The_connection_is_not_shown_and_the_schema_lists_the_table()
    {
        var (client, org, upload) = await OwnerWithUploadAsync();

        var raw = await (await client.GetAsync(Base(org))).Content.ReadAsStringAsync();
        Assert.DoesNotContain(".db", raw);

        var schema = await ReadAsync<SchemaDto>(await client.GetAsync($"{Base(org)}/{upload.DataSource.Id}/schema"));
        var table = Assert.Single(Assert.Single(schema.Schemas).Tables);
        Assert.Equal("sales", table.Name);
        Assert.Equal(["region", "amount", "sold_on"], table.Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task The_options_of_the_form_are_used()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var upload = await ReadAsync<UploadResult>(await client.PostAsync(Base(org.Id) + "/csv",
            Form("1;x\n2;y\n", "any.csv", "No header", ("TableName", "pairs"), ("HasHeader", "false"), ("Delimiter", ";"))));

        Assert.Equal("pairs", upload.TableName);
        Assert.Equal(["column_1", "column_2"], upload.Columns.Select(c => c.Name));
        Assert.Equal(2, upload.RowCount);
    }

    [Fact]
    public async Task A_name_and_a_file_are_required()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var noName = await client.PostAsync(Base(org.Id) + "/csv", Form(Sales, name: null));
        var noFile = await client.PostAsync(Base(org.Id) + "/csv", new MultipartFormDataContent { { new StringContent("x"), "Name" } });

        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a,b\n")]
    [InlineData("a,b\n1,2\n3,4,5\n")]
    public async Task A_file_that_cannot_be_used_is_refused_in_words_and_leaves_nothing_on_disk(string csv)
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var before = FilesOnDisk();

        var response = await client.PostAsync(Base(org.Id) + "/csv", Form(csv));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("File", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, FilesOnDisk());
        Assert.Empty(await ReadAsync<List<DataSourceDto>>(await client.GetAsync(Base(org.Id))));
    }

    [Fact]
    public async Task A_file_that_is_not_text_is_refused()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var form = new MultipartFormDataContent { { new StringContent("Book"), "Name" } };
        form.Add(new ByteArrayContent([0x50, 0x4B, 3, 4, 0, 0, 0, 0]), "File", "book.xlsx");

        var response = await client.PostAsync(Base(org.Id) + "/csv", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not text", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_without_being_kept()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var before = FilesOnDisk();
        var big = "a\n" + new string('x', 21 * 1024 * 1024);

        var response = await client.PostAsync(Base(org.Id) + "/csv", Form(big));

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, response.StatusCode.ToString());
        Assert.Equal(before, FilesOnDisk());
    }

    [Theory]
    [InlineData("../../evil.csv")]
    [InlineData("..\\..\\evil.csv")]
    [InlineData("/etc/passwd")]
    public async Task A_file_name_with_folders_in_it_changes_nothing_but_the_table_name(string fileName)
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var upload = await ReadAsync<UploadResult>(await client.PostAsync(Base(org.Id) + "/csv", Form(Sales, fileName)));

        Assert.Matches("^[a-z_]+$", upload.TableName);
        // Everything the CSV upload kept is directly in the uploads folder (pictures have their own "images" folder inside it).
        var pictures = Path.Combine(factory.UploadFolder, "images");
        Assert.All(Directory.GetFiles(factory.UploadFolder, "*", SearchOption.AllDirectories).Where(f => !f.StartsWith(pictures + Path.DirectorySeparatorChar)),
            f => Assert.Equal(factory.UploadFolder, Path.GetDirectoryName(f)));
    }

    [Fact]
    public async Task Viewers_and_strangers_cannot_upload()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var viewer = await AddMemberAsync(owner, org.Id, "Viewer");
        var (stranger, _, _) = await NewUserAsync(factory);
        var before = FilesOnDisk();

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync(Base(org.Id) + "/csv", Form(Sales))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync(Base(org.Id) + "/csv", Form(Sales))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateApiClient().PostAsync(Base(org.Id) + "/csv", Form(Sales))).StatusCode);
        Assert.Equal(before, FilesOnDisk());
    }

    [Fact]
    public async Task A_csv_data_source_cannot_be_made_or_tested_with_a_connection_string()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var body = new { name = "Sneaky", type = "Csv", connectionString = "Data Source=/etc/passwd" };

        var create = await client.PostAsJsonAsync(Base(org.Id), body);
        var test = await client.PostAsJsonAsync(Base(org.Id) + "/test", new { type = "Csv", connectionString = "Data Source=/etc/passwd" });

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, test.StatusCode);
        Assert.Empty(await ReadAsync<List<DataSourceDto>>(await client.GetAsync(Base(org.Id))));
    }

    [Fact]
    public async Task The_saved_connection_can_be_tested()
    {
        var (client, org, upload) = await OwnerWithUploadAsync();

        var result = await client.PostAsync($"{Base(org)}/{upload.DataSource.Id}/test", null);

        Assert.Contains("\"success\":true", await result.Content.ReadAsStringAsync());
    }

    // ---- using it -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dataset_on_an_upload_describes_its_columns_and_answers_chart_queries()
    {
        var (client, org, upload) = await OwnerWithUploadAsync();

        var dataset = await CreateDatasetAsync(client, org, upload.DataSource.Id, "SELECT region, amount, sold_on FROM sales");
        Assert.Equal(["region:text", "amount:bigint", "sold_on:date"], dataset.Columns.Select(c => $"{c.Name}:{c.DataType}"));

        var response = await QueryAsync(client, org, dataset.Id, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new object[] { new { aggregation = "Sum", column = "amount", alias = "total" } },
            order = new[] { new { position = 2, direction = "Desc" } },
        });
        var result = await ReadAsync<QueryResult>(response);

        Assert.Equal(["North", "South"], result.Rows.Select(r => r[0].GetString()));
        Assert.Equal([30d, 5d], result.Rows.Select(r => r[1].GetDouble()));
        Assert.Contains("\"region\"", result.Sql);
    }

    [Fact]
    public async Task A_time_grain_works_on_an_uploaded_date()
    {
        var (client, org, upload) = await OwnerWithUploadAsync();
        var dataset = await CreateDatasetAsync(client, org, upload.DataSource.Id, "SELECT * FROM sales");

        var result = await ReadAsync<QueryResult>(await QueryAsync(client, org, dataset.Id, new
        {
            dimensions = new[] { new { column = "sold_on", grain = "Month" } },
            measures = new object[] { new { aggregation = "Count", alias = "n" } },
        }));

        Assert.Equal(["2026-01", "2026-02"], result.Rows.Select(r => r[0].GetString()));
        Assert.Equal([2, 1], result.Rows.Select(r => r[1].GetInt32()));
    }

    [Fact]
    public async Task A_table_view_can_be_built_on_an_upload()
    {
        var (client, org, upload) = await OwnerWithUploadAsync();

        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/datasets", new
        {
            dataSourceId = upload.DataSource.Id,
            name = "Sales view",
            kind = "TableView",
            definition = new { main = new { schema = "main", name = "sales", fields = new[] { "region", "amount" } }, joins = Array.Empty<object>() },
        });

        var dataset = await ReadAsync<DatasetDto>(response);
        Assert.Equal(["region", "amount"], dataset.Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task Sql_that_writes_or_reaches_for_other_files_is_refused_and_the_data_stays()
    {
        var (client, org, upload) = await OwnerWithUploadAsync();
        var preview = $"{Base(org)}/{upload.DataSource.Id}/preview";

        foreach (var sql in new[]
        {
            "DELETE FROM sales",
            "DROP TABLE sales",
            "ATTACH DATABASE '/etc/passwd' AS x",
            "PRAGMA writable_schema = 1",
            "SELECT load_extension('x')",
            "SELECT readfile('/etc/passwd')",
            "SELECT * FROM (SELECT readfile('/etc/passwd')) t",
            "SELECT * FROM sqlite_dbpage",
        })
        {
            var response = await client.PostAsJsonAsync(preview, new { sql });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var still = await client.PostAsJsonAsync(preview, new { sql = "SELECT count(*) AS n FROM sales" });
        Assert.Contains("3", await still.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_query_for_something_that_is_not_in_the_file_says_so()
    {
        var (client, org, upload) = await OwnerWithUploadAsync();

        var response = await client.PostAsJsonAsync($"{Base(org)}/{upload.DataSource.Id}/preview", new { sql = "SELECT nope FROM sales" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no such column: nope", await response.Content.ReadAsStringAsync());
    }

    // ---- replacing and deleting -------------------------------------------------------------------------------

    [Fact]
    public async Task A_new_file_replaces_the_data_and_what_was_built_on_the_table_carries_on()
    {
        var (client, org, upload) = await OwnerWithUploadAsync();
        var dataset = await CreateDatasetAsync(client, org, upload.DataSource.Id, "SELECT region, amount FROM sales");
        var before = FilesOnDisk();

        var replaced = await ReadAsync<UploadResult>(await client.PutAsync(
            $"{Base(org)}/{upload.DataSource.Id}/csv",
            Form("region,amount,sold_on\nWest,100,2026-05-01\nWest,50,2026-05-02\nEast,1,2026-05-03\nEast,2,2026-05-04\n", "different-name.csv", null)));

        Assert.Equal("sales", replaced.TableName); // not "different_name"
        Assert.Equal(4, replaced.RowCount);
        Assert.Equal(before, FilesOnDisk()); // the old file went

        var result = await ReadAsync<QueryResult>(await QueryAsync(client, org, dataset.Id, new
        {
            dimensions = new[] { new { column = "region" } },
            measures = new object[] { new { aggregation = "Sum", column = "amount", alias = "total" } },
            order = new[] { new { position = 1, direction = "Asc" } },
        }));
        Assert.Equal(["East", "West"], result.Rows.Select(r => r[0].GetString()));
        Assert.Equal([3d, 150d], result.Rows.Select(r => r[1].GetDouble()));
    }

    [Fact]
    public async Task A_bad_replacement_changes_nothing()
    {
        var (client, org, upload) = await OwnerWithUploadAsync();
        var dataset = await CreateDatasetAsync(client, org, upload.DataSource.Id, "SELECT * FROM sales");
        var before = FilesOnDisk();

        var response = await client.PutAsync($"{Base(org)}/{upload.DataSource.Id}/csv", Form("a,b\n", "x.csv", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, FilesOnDisk());
        var result = await ReadAsync<QueryResult>(await QueryAsync(client, org, dataset.Id, new { measures = new[] { new { aggregation = "Count", alias = "n" } } }));
        Assert.Equal(3, result.Rows[0][0].GetInt32());
    }

    [Fact]
    public async Task Only_a_csv_data_source_has_its_file_replaced()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var pg = await ReadAsync<DataSourceDto>(await client.PostAsJsonAsync(Base(org.Id),
            new { name = "Shop DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() }));

        var response = await client.PutAsync($"{Base(org.Id)}/{pg.Id}/csv", Form(Sales, "x.csv", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_removes_the_data_source_its_datasets_and_its_file()
    {
        var (client, org, upload) = await OwnerWithUploadAsync();
        var dataset = await CreateDatasetAsync(client, org, upload.DataSource.Id, "SELECT * FROM sales");
        var before = FilesOnDisk();

        var response = await client.DeleteAsync($"{Base(org)}/{upload.DataSource.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(before - 1, FilesOnDisk());
        Assert.DoesNotContain(await ReadAsync<List<DataSourceDto>>(await client.GetAsync(Base(org))), d => d.Id == upload.DataSource.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await QueryAsync(client, org, dataset.Id, new { measures = new[] { new { aggregation = "Count", alias = "n" } } })).StatusCode);
    }

    [Fact]
    public async Task Only_an_owner_can_delete_and_an_editor_can_replace()
    {
        var (owner, org, upload) = await OwnerWithUploadAsync();
        var editor = await AddMemberAsync(owner, org, "Editor");
        var viewer = await AddMemberAsync(owner, org, "Viewer");

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.DeleteAsync($"{Base(org)}/{upload.DataSource.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"{Base(org)}/{upload.DataSource.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsync($"{Base(org)}/{upload.DataSource.Id}/csv", Form(Sales, "x.csv", null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.PutAsync($"{Base(org)}/{upload.DataSource.Id}/csv", Form(Sales, "x.csv", null))).StatusCode);
    }

    [Fact]
    public async Task Another_organization_cannot_reach_the_data_source()
    {
        var (_, org, upload) = await OwnerWithUploadAsync();
        var (other, _, _) = await NewUserAsync(factory);
        var otherOrg = await CreateOrgAsync(other);

        // The id belongs to the first organization; asking through the second finds nothing.
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Base(otherOrg.Id)}/{upload.DataSource.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"{Base(otherOrg.Id)}/{upload.DataSource.Id}/csv", Form(Sales, "x.csv", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Base(otherOrg.Id)}/{upload.DataSource.Id}/schema")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"{Base(otherOrg.Id)}/{upload.DataSource.Id}/preview", new { sql = "SELECT 1" })).StatusCode);
        Assert.True(FilesOnDisk() > 0 && org != otherOrg.Id);
    }
}
