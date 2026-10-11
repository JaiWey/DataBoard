using System.Net;
using System.Net.Http.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

public class DefaultOrganizationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Org(Guid Id, string Name, string Role, bool IsDefault);

    private static async Task<List<Org>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Org>>("/api/organizations", Json))!;

    [Fact]
    public async Task Everyone_who_signs_up_is_in_the_one_default_organization_listed_first()
    {
        var (first, _, _) = await NewUserAsync(factory);
        var (second, _, _) = await NewUserAsync(factory);
        await CreateOrgAsync(second, "Mine");

        var a = await ListAsync(first);
        var b = await ListAsync(second);

        var defaultA = Assert.Single(a, o => o.IsDefault);
        Assert.Equal("Default", defaultA.Name);
        Assert.Equal(defaultA.Id, Assert.Single(b, o => o.IsDefault).Id);
        Assert.True(b[0].IsDefault);
        Assert.Contains(b, o => o.Name == "Mine" && !o.IsDefault);
        Assert.Contains(defaultA.Role, new[] { "Owner", "Editor" });
    }

    [Fact]
    public async Task Nobody_can_leave_or_be_removed_from_the_default_organization()
    {
        var (client, _, user) = await NewUserAsync(factory);
        var def = (await ListAsync(client)).Single(o => o.IsDefault);

        var leave = await client.DeleteAsync($"/api/organizations/{def.Id}/members/{user.Id}");

        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
        Assert.Contains(await ListAsync(client), o => o.IsDefault);
    }
}
