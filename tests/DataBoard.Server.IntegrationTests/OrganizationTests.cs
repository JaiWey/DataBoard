using System.Net;
using System.Net.Http.Json;
using DataBoard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OrganizationTests(ApiFactory factory)
{
    private static object NewDataSource(string name = "Sales", string cs = "Host=secret-host;Database=sales;Username=reader;Password=hunter2") =>
        new { name, type = "PostgreSql", connectionString = cs };

    [Fact]
    public async Task Creator_is_owner_and_sees_the_organization()
    {
        var (client, _, _) = await NewUserAsync(factory);

        var org = await CreateOrgAsync(client, "Acme");
        var mine = await client.GetFromJsonAsync<List<OrgDto>>("/api/organizations", Json);

        Assert.Equal("Owner", org.Role);
        Assert.Contains(mine!, o => o.Id == org.Id);
    }

    [Fact]
    public async Task Blank_organization_name_is_400()
    {
        var (client, _, _) = await NewUserAsync(factory);

        var response = await client.PostAsJsonAsync("/api/organizations", new { name = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Users_only_see_their_own_organizations()
    {
        var (alice, _, _) = await NewUserAsync(factory);
        var (bob, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(alice);

        var bobsOrgs = await bob.GetFromJsonAsync<List<OrgDto>>("/api/organizations", Json);

        Assert.DoesNotContain(bobsOrgs!, o => o.Id == org.Id);
    }

    [Fact]
    public async Task Data_source_is_created_listed_and_never_exposes_or_stores_the_plain_connection_string()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);

        var created = await client.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources", NewDataSource());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hunter2", createdBody);

        var listBody = await client.GetStringAsync($"/api/organizations/{org.Id}/data-sources");
        Assert.Contains("Sales", listBody);
        Assert.DoesNotContain("hunter2", listBody);
        Assert.DoesNotContain("secret-host", listBody);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.DataSources.AsNoTracking().SingleAsync(d => d.OrganizationId == org.Id);
        Assert.DoesNotContain("hunter2", stored.EncryptedConnectionString);
    }

    [Fact]
    public async Task Non_members_get_404_for_list_and_create()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var (stranger, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);

        var list = await stranger.GetAsync($"/api/organizations/{org.Id}/data-sources");
        var create = await stranger.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources", NewDataSource());

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
    }

    [Fact]
    public async Task Viewer_can_list_but_not_create_and_cannot_add_members()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var (viewer, viewerEmail, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);

        var add = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/members", new { email = viewerEmail, role = "Viewer" });
        Assert.Equal(HttpStatusCode.NoContent, add.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/organizations/{org.Id}/data-sources")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources", NewDataSource())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync($"/api/organizations/{org.Id}/members", new { email = NewEmail(), role = "Viewer" })).StatusCode);
    }

    [Fact]
    public async Task Editor_can_create_data_sources_and_duplicate_member_is_409()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var (editor, editorEmail, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/members", new { email = editorEmail, role = "Editor" });

        var created = await editor.PostAsJsonAsync($"/api/organizations/{org.Id}/data-sources", NewDataSource());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var again = await owner.PostAsJsonAsync($"/api/organizations/{org.Id}/members", new { email = editorEmail, role = "Viewer" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Duplicate_data_source_name_in_one_organization_is_rejected()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(client);
        var url = $"/api/organizations/{org.Id}/data-sources";

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(url, NewDataSource("Same"))).StatusCode);

        var second = await client.PostAsJsonAsync(url, NewDataSource("Same"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
