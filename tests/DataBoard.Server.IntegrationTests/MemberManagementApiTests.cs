using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Seeing who is in an organization, changing their role, and removing them (or leaving).</summary>
[Collection(ApiCollection.Name)]
public class MemberManagementApiTests(ApiFactory factory)
{
    private sealed record Org(HttpClient Owner, UserDto OwnerUser, Guid Id);

    private static string Members(Org o, Guid? user = null) =>
        $"/api/organizations/{o.Id}/members" + (user is null ? "" : $"/{user}");

    private async Task<Org> NewOrgAsync()
    {
        var (owner, _, user) = await NewUserAsync(factory);
        return new Org(owner, user, (await CreateOrgAsync(owner, "Team")).Id);
    }

    /// <summary>Someone with an account, added to the organization with this role; a client that is signed in as them.</summary>
    private async Task<(HttpClient Client, UserDto User)> JoinAsync(Org o, string role)
    {
        var (client, email, user) = await NewUserAsync(factory);
        (await o.Owner.PostAsJsonAsync(Members(o), new { email, role })).EnsureSuccessStatusCode();
        return (client, user);
    }

    private static async Task<JsonElement[]> ListAsync(HttpClient client, Org o)
    {
        var response = await client.GetAsync(Members(o));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
    }

    private static string RoleOf(JsonElement[] members, Guid user) =>
        members.Single(m => m.GetProperty("userId").GetGuid() == user).GetProperty("role").GetString()!;

    [Fact]
    public async Task Every_member_sees_who_is_in_the_organization_with_owners_first()
    {
        var o = await NewOrgAsync();
        var (viewer, viewerUser) = await JoinAsync(o, "Viewer");
        var (_, editorUser) = await JoinAsync(o, "Editor");

        var seenByViewer = await ListAsync(viewer, o);

        Assert.Equal(3, seenByViewer.Length);
        Assert.Equal(o.OwnerUser.Id, seenByViewer[0].GetProperty("userId").GetGuid());
        Assert.Equal("Owner", seenByViewer[0].GetProperty("role").GetString());
        Assert.Equal(editorUser.Id, seenByViewer[1].GetProperty("userId").GetGuid());
        Assert.Equal(viewerUser.Email, seenByViewer[2].GetProperty("email").GetString());
        Assert.Equal("Test User", seenByViewer[2].GetProperty("displayName").GetString());
        Assert.True(seenByViewer[2].GetProperty("joinedUtc").GetDateTime() > DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task The_list_never_shows_anything_secret_and_only_this_organizations_people()
    {
        var o = await NewOrgAsync();
        var other = await NewOrgAsync();

        var text = await o.Owner.GetStringAsync(Members(o));

        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(other.OwnerUser.Email, text);
    }

    [Fact]
    public async Task Someone_outside_the_organization_gets_not_found_for_all_three_and_anonymous_gets_nothing()
    {
        var o = await NewOrgAsync();
        var (stranger, _, _) = await NewUserAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Members(o))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync(Members(o, o.OwnerUser.Id), new { role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync(Members(o, o.OwnerUser.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateApiClient().GetAsync(Members(o))).StatusCode);
    }

    [Fact]
    public async Task An_owner_promotes_and_demotes_and_the_new_role_applies_at_once()
    {
        var o = await NewOrgAsync();
        var (viewer, viewerUser) = await JoinAsync(o, "Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/organizations/{o.Id}/folders?kind=Dataset", new { name = "x" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await o.Owner.PutAsJsonAsync(Members(o, viewerUser.Id), new { role = "Editor" })).StatusCode);

        Assert.Equal("Editor", RoleOf(await ListAsync(o.Owner, o), viewerUser.Id));
        Assert.Equal(HttpStatusCode.Created, (await viewer.PostAsJsonAsync($"/api/organizations/{o.Id}/folders?kind=Dataset", new { name = "x" })).StatusCode);

        await o.Owner.PutAsJsonAsync(Members(o, viewerUser.Id), new { role = "Viewer" });
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/organizations/{o.Id}/folders?kind=Dataset", new { name = "y" })).StatusCode);
    }

    [Theory]
    [InlineData("Editor")]
    [InlineData("Viewer")]
    public async Task Only_an_owner_can_change_roles_or_remove_somebody_else(string role)
    {
        var o = await NewOrgAsync();
        var (caller, _) = await JoinAsync(o, role);
        var (_, target) = await JoinAsync(o, "Viewer");

        Assert.Equal(HttpStatusCode.Forbidden, (await caller.PutAsJsonAsync(Members(o, target.Id), new { role = "Owner" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.DeleteAsync(Members(o, target.Id))).StatusCode);
        Assert.Equal("Viewer", RoleOf(await ListAsync(o.Owner, o), target.Id));
    }

    [Theory]
    [InlineData("""{ "role": "Boss" }""")]
    [InlineData("""{ "role": 0 }""")]
    [InlineData("""{ "role": 9 }""")]
    [InlineData("""{}""")]
    public async Task An_unknown_role_is_refused(string body)
    {
        var o = await NewOrgAsync();
        var (_, user) = await JoinAsync(o, "Viewer");

        var response = await o.Owner.PutAsync(Members(o, user.Id), new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Viewer", RoleOf(await ListAsync(o.Owner, o), user.Id));
    }

    [Fact]
    public async Task Changing_or_removing_someone_who_is_not_a_member_is_not_found()
    {
        var o = await NewOrgAsync();
        var (_, outsider, _) = await NewUserAsync(factory);
        _ = outsider;

        Assert.Equal(HttpStatusCode.NotFound, (await o.Owner.PutAsJsonAsync(Members(o, Guid.NewGuid()), new { role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await o.Owner.DeleteAsync(Members(o, Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task The_last_owner_cannot_be_demoted_removed_or_leave_but_can_once_there_is_another()
    {
        var o = await NewOrgAsync();
        var (_, second) = await JoinAsync(o, "Editor");

        foreach (var response in new[]
        {
            await o.Owner.PutAsJsonAsync(Members(o, o.OwnerUser.Id), new { role = "Editor" }),
            await o.Owner.DeleteAsync(Members(o, o.OwnerUser.Id)),
        })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("at least one Owner", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal("Owner", RoleOf(await ListAsync(o.Owner, o), o.OwnerUser.Id));

        await o.Owner.PutAsJsonAsync(Members(o, second.Id), new { role = "Owner" });
        Assert.Equal(HttpStatusCode.NoContent, (await o.Owner.PutAsJsonAsync(Members(o, o.OwnerUser.Id), new { role = "Viewer" })).StatusCode);
        Assert.Equal("Viewer", RoleOf(await ListAsync(o.Owner, o), o.OwnerUser.Id));
    }

    [Fact]
    public async Task A_removed_member_loses_access_at_once_even_with_a_valid_token_and_is_not_listed()
    {
        var o = await NewOrgAsync();
        var (member, user) = await JoinAsync(o, "Editor");
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/organizations/{o.Id}/datasets")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await o.Owner.DeleteAsync(Members(o, user.Id))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/organizations/{o.Id}/datasets")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(Members(o))).StatusCode);
        Assert.DoesNotContain(await ListAsync(o.Owner, o), m => m.GetProperty("userId").GetGuid() == user.Id);
        var theirs = await member.GetFromJsonAsync<JsonElement>("/api/organizations");
        Assert.DoesNotContain(theirs.EnumerateArray(), x => x.GetProperty("id").GetGuid() == o.Id);
    }

    [Fact]
    public async Task What_a_removed_member_made_stays_and_they_can_be_added_back()
    {
        var o = await NewOrgAsync();
        var (member, user) = await JoinAsync(o, "Editor");
        var folder = await member.PostAsJsonAsync($"/api/organizations/{o.Id}/folders?kind=Dataset", new { name = "Theirs" });
        folder.EnsureSuccessStatusCode();

        await o.Owner.DeleteAsync(Members(o, user.Id));

        var folders = await o.Owner.GetFromJsonAsync<JsonElement>($"/api/organizations/{o.Id}/folders?kind=Dataset");
        Assert.Contains(folders.EnumerateArray(), f => f.GetProperty("name").GetString() == "Theirs");

        Assert.Equal(HttpStatusCode.NoContent, (await o.Owner.PostAsJsonAsync(Members(o), new { email = user.Email, role = "Viewer" })).StatusCode);
        Assert.Equal("Viewer", RoleOf(await ListAsync(o.Owner, o), user.Id));
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Editor")]
    public async Task Anyone_can_leave_and_the_organization_goes_on(string role)
    {
        var o = await NewOrgAsync();
        var (member, user) = await JoinAsync(o, role);

        Assert.Equal(HttpStatusCode.NoContent, (await member.DeleteAsync(Members(o, user.Id))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/organizations/{o.Id}/datasets")).StatusCode);
        Assert.Single(await ListAsync(o.Owner, o));
    }

    [Fact]
    public async Task A_new_member_added_by_email_shows_in_the_list_and_a_second_add_is_a_conflict()
    {
        var o = await NewOrgAsync();
        var (_, email, user) = await NewUserAsync(factory);

        Assert.Equal(HttpStatusCode.NoContent, (await o.Owner.PostAsJsonAsync(Members(o), new { email, role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.Owner.PostAsJsonAsync(Members(o), new { email, role = "Editor" })).StatusCode);

        var list = await ListAsync(o.Owner, o);
        Assert.Equal("Viewer", RoleOf(list, user.Id));
    }
}
