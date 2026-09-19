using System.Net;
using System.Net.Http.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DatasetFolderApiTests(ApiFactory factory)
{
    private sealed record FolderDto(Guid Id, Guid? ParentId, string Name);
    private sealed record DatasetDto(Guid Id, string Name, Guid? FolderId);

    private static string Url(Guid org, string suffix = "") => $"/api/organizations/{org}/dataset-folders{suffix}";
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

    private static async Task<FolderDto> CreateFolderAsync(HttpClient client, Guid org, string name, Guid? parent = null)
    {
        var response = await client.PostAsJsonAsync(Url(org), new { name, parentId = parent });
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

        var listed = (await client.GetFromJsonAsync<List<FolderDto>>(Url(org), Json))!;
        Assert.Equal(["Archive", "Europe", "Sales"], listed.Select(f => f.Name));
        Assert.Equal(sales.Id, listed.Single(f => f.Name == "Europe").ParentId);

        var renamed = await client.PutAsJsonAsync(Url(org, $"/{europe.Id}"), new { name = "EMEA", parentId = archive.Id });
        var moved = (await renamed.Content.ReadFromJsonAsync<FolderDto>(Json))!;
        Assert.Equal(("EMEA", archive.Id), (moved.Name, moved.ParentId));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Url(org, $"/{europe.Id}"))).StatusCode);
        Assert.DoesNotContain(
            (await client.GetFromJsonAsync<List<FolderDto>>(Url(org), Json))!, f => f.Id == europe.Id);
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

        Assert.Single((await viewer.GetFromJsonAsync<List<FolderDto>>(Url(org), Json))!);
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
}
