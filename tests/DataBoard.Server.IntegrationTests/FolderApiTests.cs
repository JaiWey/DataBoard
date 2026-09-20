using System.Net;
using System.Net.Http.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

[Collection(ApiCollection.Name)]
public class FolderApiTests(ApiFactory factory)
{
    private sealed record FolderDto(Guid Id, Guid? ParentId, string Name, string Kind);
    private sealed record ItemDto(Guid Id, string Name, Guid? FolderId);
    private sealed record DatasetDto(Guid Id, string Name, Guid? FolderId);

    private static string Url(Guid org, string suffix = "") => $"/api/organizations/{org}/folders{suffix}";
    private static string DatasetsUrl(Guid org, string suffix = "") => $"/api/organizations/{org}/datasets{suffix}";

    private async Task<(HttpClient Client, Guid Org, Guid Source)> OwnerWithSourceAsync()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var response = await client.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources",
            new { name = "Sales DB", type = "PostgreSql", connectionString = factory.TargetConnectionString() });
        response.EnsureSuccessStatusCode();
        var source = (await response.Content.ReadFromJsonAsync<DataSourceDto>(Json))!;
        return (client, org.Id, source.Id);
    }

    private static async Task<FolderDto> CreateFolderAsync(HttpClient client, Guid org, string name, Guid? parent = null, string kind = "Dataset")
    {
        var response = await client.PostAsJsonAsync(Url(org), new { name, parentId = parent, kind });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FolderDto>(Json))!;
    }

    private static async Task<DatasetDto> CreateDatasetAsync(HttpClient client, Guid org, Guid source, string name, Guid? folder = null)
    {
        var response = await client.PostAsJsonAsync(DatasetsUrl(org),
            new { dataSourceId = source, name, sql = "SELECT id, region FROM sales", folderId = folder });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DatasetDto>(Json))!;
    }

    private async Task<HttpClient> AddMemberAsync(HttpClient owner, Guid org, string role)
    {
        var (member, email, _) = await NewUserAsync(factory);
        (await owner.PostAsJsonAsync($"/api/organizations/{org}/members", new { email, role })).EnsureSuccessStatusCode();
        return member;
    }

    [Fact]
    public async Task Folders_are_created_listed_renamed_moved_and_deleted()
    {
        var (client, org, _) = await OwnerWithSourceAsync();

        var sales = await CreateFolderAsync(client, org, "Sales");
        var europe = await CreateFolderAsync(client, org, "Europe", sales.Id);
        var archive = await CreateFolderAsync(client, org, "Archive");

        var listed = (await client.GetFromJsonAsync<List<FolderDto>>(Url(org) + "?kind=Dataset", Json))!;
        Assert.Equal(["Archive", "Europe", "Sales"], listed.Select(f => f.Name));
        Assert.Equal(sales.Id, listed.Single(f => f.Name == "Europe").ParentId);

        var renamed = await client.PutAsJsonAsync(Url(org, $"/{europe.Id}"), new { name = "EMEA", parentId = archive.Id });
        var moved = (await renamed.Content.ReadFromJsonAsync<FolderDto>(Json))!;
        Assert.Equal(("EMEA", archive.Id), (moved.Name, moved.ParentId));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Url(org, $"/{europe.Id}"))).StatusCode);
        Assert.DoesNotContain(
            (await client.GetFromJsonAsync<List<FolderDto>>(Url(org) + "?kind=Dataset", Json))!, f => f.Id == europe.Id);
    }

    [Fact]
    public async Task Duplicate_sibling_names_conflict_and_a_cycle_or_missing_parent_is_a_bad_request()
    {
        var (client, org, _) = await OwnerWithSourceAsync();
        var a = await CreateFolderAsync(client, org, "A");
        var b = await CreateFolderAsync(client, org, "B", a.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Url(org), new { name = "a" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(Url(org), new { name = "X", parentId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync(Url(org, $"/{a.Id}"), new { name = "A", parentId = b.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(Url(org), new { name = "bad/name" })).StatusCode);
    }

    [Fact]
    public async Task Nesting_stops_at_eight_levels()
    {
        var (client, org, _) = await OwnerWithSourceAsync();
        Guid? parent = null;
        for (var level = 1; level <= 8; level++)
            parent = (await CreateFolderAsync(client, org, $"L{level}", parent)).Id;

        var tooDeep = await client.PostAsJsonAsync(Url(org), new { name = "L9", parentId = parent });

        Assert.Equal(HttpStatusCode.BadRequest, tooDeep.StatusCode);
    }

    [Fact]
    public async Task Datasets_are_created_in_a_folder_moved_between_folders_and_back_to_the_top()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var sales = await CreateFolderAsync(client, org, "Sales");
        var ops = await CreateFolderAsync(client, org, "Ops");

        var dataset = await CreateDatasetAsync(client, org, source, "Orders", sales.Id);
        var plain = await CreateDatasetAsync(client, org, source, "Plain");
        Assert.Equal(sales.Id, dataset.FolderId);
        Assert.Null(plain.FolderId);

        var moved = await client.PutAsJsonAsync(DatasetsUrl(org, $"/{dataset.Id}/folder"), new { folderId = ops.Id });
        Assert.Equal(ops.Id, (await moved.Content.ReadFromJsonAsync<DatasetDto>(Json))!.FolderId);
        var top = await client.PutAsJsonAsync(DatasetsUrl(org, $"/{dataset.Id}/folder"), new { folderId = (Guid?)null });
        Assert.Null((await top.Content.ReadFromJsonAsync<DatasetDto>(Json))!.FolderId);

        var listed = (await client.GetFromJsonAsync<List<DatasetDto>>(DatasetsUrl(org), Json))!;
        Assert.All(listed, d => Assert.Null(d.FolderId));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync(DatasetsUrl(org, $"/{dataset.Id}/folder"), new { folderId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(DatasetsUrl(org),
                new { dataSourceId = source, name = "Lost", sql = "SELECT id FROM sales", folderId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task A_folder_with_a_dataset_or_a_subfolder_cannot_be_deleted_until_it_is_emptied()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var sales = await CreateFolderAsync(client, org, "Sales");
        var europe = await CreateFolderAsync(client, org, "Europe", sales.Id);
        var dataset = await CreateDatasetAsync(client, org, source, "Orders", europe.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(Url(org, $"/{sales.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(Url(org, $"/{europe.Id}"))).StatusCode);

        (await client.DeleteAsync(DatasetsUrl(org, $"/{dataset.Id}"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Url(org, $"/{europe.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Url(org, $"/{sales.Id}"))).StatusCode);
    }

    [Fact]
    public async Task Viewers_see_folders_but_cannot_change_them_and_other_organizations_see_nothing()
    {
        var (owner, org, source) = await OwnerWithSourceAsync();
        var sales = await CreateFolderAsync(owner, org, "Sales");
        var dataset = await CreateDatasetAsync(owner, org, source, "Orders");
        var viewer = await AddMemberAsync(owner, org, "Viewer");

        Assert.Single((await viewer.GetFromJsonAsync<List<FolderDto>>(Url(org) + "?kind=Dataset", Json))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(Url(org), new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PutAsJsonAsync(Url(org, $"/{sales.Id}"), new { name = "Nope", parentId = (Guid?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync(Url(org, $"/{sales.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PutAsJsonAsync(DatasetsUrl(org, $"/{dataset.Id}/folder"), new { folderId = sales.Id })).StatusCode);

        var (stranger, _, _) = await NewUserAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Url(org))).StatusCode);
    }

    [Fact]
    public async Task A_folder_of_another_organization_cannot_be_used()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var (otherClient, otherOrg, _) = await OwnerWithSourceAsync();
        var theirs = await CreateFolderAsync(otherClient, otherOrg, "Theirs");
        var dataset = await CreateDatasetAsync(client, org, source, "Orders");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url(org), new { name = "X", parentId = theirs.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync(DatasetsUrl(org, $"/{dataset.Id}/folder"), new { folderId = theirs.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(Url(org, $"/{theirs.Id}"))).StatusCode);
    }

    // ---- charts and dashboards have their own trees --------------------------------------------------------

    private static async Task<ItemDto> CreateChartAsync(HttpClient client, Guid org, Guid dataset, string name, Guid? folder = null)
    {
        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/charts", new
        {
            name, datasetId = dataset, type = "Number", folderId = folder,
            definition = new { query = new { measures = new[] { new { aggregation = "Count", alias = "n" } } } },
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ItemDto>(Json))!;
    }

    private static async Task<ItemDto> CreateDashboardAsync(HttpClient client, Guid org, string name, Guid? folder = null)
    {
        var response = await client.PostAsJsonAsync($"/api/organizations/{org}/dashboards", new { name, folderId = folder, definition = new { widgets = Array.Empty<object>(), filters = Array.Empty<object>() } });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ItemDto>(Json))!;
    }

    [Fact]
    public async Task Each_kind_lists_only_its_own_folders_and_the_same_name_can_be_used_in_each()
    {
        var (client, org, _) = await OwnerWithSourceAsync();

        await CreateFolderAsync(client, org, "Sales", null, "Dataset");
        var charts = await CreateFolderAsync(client, org, "Sales", null, "Chart");
        await CreateFolderAsync(client, org, "Sales", null, "Dashboard");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Url(org), new { name = "SALES", kind = "Chart" })).StatusCode);

        foreach (var kind in new[] { "Dataset", "Chart", "Dashboard" })
            Assert.Equal(kind, Assert.Single((await client.GetFromJsonAsync<List<FolderDto>>(Url(org) + $"?kind={kind}", Json))!).Kind);
        Assert.Equal("Chart", charts.Kind);
        // Without a kind the list is the dataset folders, as it always was.
        Assert.Equal("Dataset", Assert.Single((await client.GetFromJsonAsync<List<FolderDto>>(Url(org), Json))!).Kind);
    }

    [Fact]
    public async Task A_folder_cannot_contain_another_kind_of_folder_or_item()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var datasetFolder = await CreateFolderAsync(client, org, "Datasets");
        var chartFolder = await CreateFolderAsync(client, org, "Charts", null, "Chart");
        var dataset = await CreateDatasetAsync(client, org, source, "Orders");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(Url(org), new { name = "X", parentId = datasetFolder.Id, kind = "Chart" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync(DatasetsUrl(org, $"/{dataset.Id}/folder"), new { folderId = chartFolder.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/organizations/{org}/dashboards", new { name = "D", folderId = chartFolder.Id })).StatusCode);
    }

    [Fact]
    public async Task Charts_are_created_in_a_folder_moved_around_and_keep_their_folder_when_edited()
    {
        var (client, org, source) = await OwnerWithSourceAsync();
        var dataset = await CreateDatasetAsync(client, org, source, "Orders");
        var sales = await CreateFolderAsync(client, org, "Sales", null, "Chart");
        var ops = await CreateFolderAsync(client, org, "Ops", null, "Chart");

        var chart = await CreateChartAsync(client, org, dataset.Id, "Orders count", sales.Id);
        Assert.Equal(sales.Id, chart.FolderId);

        var moved = await client.PutAsJsonAsync($"/api/organizations/{org}/charts/{chart.Id}/folder", new { folderId = ops.Id });
        Assert.Equal(ops.Id, (await moved.Content.ReadFromJsonAsync<ItemDto>(Json))!.FolderId);

        var edited = await client.PutAsJsonAsync($"/api/organizations/{org}/charts/{chart.Id}", new
        {
            name = "Orders count", datasetId = dataset.Id, type = "Number",
            definition = new { query = new { measures = new[] { new { aggregation = "Count", alias = "n" } } } },
        });
        Assert.Equal(ops.Id, (await edited.Content.ReadFromJsonAsync<ItemDto>(Json))!.FolderId);

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(Url(org, $"/{ops.Id}"))).StatusCode);
        var top = await client.PutAsJsonAsync($"/api/organizations/{org}/charts/{chart.Id}/folder", new { folderId = (Guid?)null });
        Assert.Null((await top.Content.ReadFromJsonAsync<ItemDto>(Json))!.FolderId);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Url(org, $"/{ops.Id}"))).StatusCode);
    }

    [Fact]
    public async Task Dashboards_are_created_in_a_folder_moved_around_and_keep_their_folder_when_edited()
    {
        var (client, org, _) = await OwnerWithSourceAsync();
        var sales = await CreateFolderAsync(client, org, "Sales", null, "Dashboard");
        var ops = await CreateFolderAsync(client, org, "Ops", null, "Dashboard");

        var dashboard = await CreateDashboardAsync(client, org, "Weekly", sales.Id);
        Assert.Equal(sales.Id, dashboard.FolderId);
        var listed = (await client.GetFromJsonAsync<List<ItemDto>>($"/api/organizations/{org}/dashboards", Json))!;
        Assert.Equal(sales.Id, Assert.Single(listed).FolderId);

        var moved = await client.PutAsJsonAsync($"/api/organizations/{org}/dashboards/{dashboard.Id}/folder", new { folderId = ops.Id });
        Assert.Equal(ops.Id, (await moved.Content.ReadFromJsonAsync<ItemDto>(Json))!.FolderId);
        var edited = await client.PutAsJsonAsync($"/api/organizations/{org}/dashboards/{dashboard.Id}",
            new { name = "Weekly report", definition = new { widgets = Array.Empty<object>(), filters = Array.Empty<object>() } });
        Assert.Equal(ops.Id, (await edited.Content.ReadFromJsonAsync<ItemDto>(Json))!.FolderId);

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(Url(org, $"/{ops.Id}"))).StatusCode);
    }

    [Fact]
    public async Task Viewers_cannot_file_charts_or_dashboards()
    {
        var (owner, org, source) = await OwnerWithSourceAsync();
        var dataset = await CreateDatasetAsync(owner, org, source, "Orders");
        var chartFolder = await CreateFolderAsync(owner, org, "Charts", null, "Chart");
        var chart = await CreateChartAsync(owner, org, dataset.Id, "Count");
        var dashboard = await CreateDashboardAsync(owner, org, "Weekly");
        var viewer = await AddMemberAsync(owner, org, "Viewer");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PutAsJsonAsync($"/api/organizations/{org}/charts/{chart.Id}/folder", new { folderId = chartFolder.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PutAsJsonAsync($"/api/organizations/{org}/dashboards/{dashboard.Id}/folder", new { folderId = (Guid?)null })).StatusCode);
    }
}