using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataBoard.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Personal access tokens: who can make them, that only a hash is kept, and that they open the MCP endpoint and nothing else.</summary>
[Collection(ApiCollection.Name)]
public class ApiTokenApiTests(ApiFactory factory)
{
    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, object? body = null) =>
        client.PostAsJsonAsync("/api/me/tokens", body ?? new { name = "Claude Desktop", scope = "Write", expiresInDays = 30 });

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static HttpClient WithToken(ApiFactory factory, string token)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task A_signed_in_person_makes_a_token_and_the_list_never_shows_its_secret_again()
    {
        var (client, _, _) = await NewUserAsync(factory);

        var before = DateTime.UtcNow;
        var created = await ReadAsync(await CreateAsync(client, new { name = "  Claude Code  ", scope = "Read", expiresInDays = 7 }));

        var secret = created.GetProperty("token").GetString()!;
        Assert.StartsWith("dbp_", secret);
        Assert.Equal(47, secret.Length); // the prefix and 256 bits as base64url
        Assert.Matches("^dbp_[A-Za-z0-9_-]{43}$", secret);
        Assert.Equal("Claude Code", created.GetProperty("name").GetString());
        Assert.Equal("Read", created.GetProperty("scope").GetString());
        Assert.InRange(created.GetProperty("expiresUtc").GetDateTime(), before.AddDays(7).AddMinutes(-1), DateTime.UtcNow.AddDays(7).AddMinutes(1));

        var list = await client.GetStringAsync("/api/me/tokens");
        Assert.Contains("Claude Code", list);
        Assert.DoesNotContain(secret, list);
        Assert.DoesNotContain("token\"", list, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Only_the_hash_of_the_secret_is_kept()
    {
        var (client, _, _) = await NewUserAsync(factory);
        var created = await ReadAsync(await CreateAsync(client));
        var secret = created.GetProperty("token").GetString()!;

        using var scope = factory.Services.CreateScope();
        var stored = scope.ServiceProvider.GetRequiredService<AppDbContext>().ApiTokens.Single(t => t.Id == created.GetProperty("id").GetGuid()).TokenHash;

        Assert.NotEqual(secret, stored);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant(), stored);
    }

    [Fact]
    public async Task A_token_is_never_valid_forever_unless_that_was_asked_for()
    {
        var (client, _, _) = await NewUserAsync(factory);

        foreach (var body in new object[]
        {
            new { name = "x", scope = "Write" },
            new { name = "x", scope = "Write", expiresInDays = 0 },
            new { name = "x", scope = "Write", expiresInDays = 366 },
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(client, body)).StatusCode);

        var forever = await ReadAsync(await CreateAsync(client, new { name = "ci", scope = "Read", neverExpires = true }));
        Assert.Equal(JsonValueKind.Null, forever.GetProperty("expiresUtc").ValueKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_token_needs_a_name(string name)
    {
        var (client, _, _) = await NewUserAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(client, new { name, scope = "Write", expiresInDays = 30 })).StatusCode);
    }

    [Fact]
    public async Task A_person_has_at_most_twenty_tokens()
    {
        var (client, _, _) = await NewUserAsync(factory);
        for (var i = 0; i < 20; i++) (await CreateAsync(client, new { name = $"t{i}", scope = "Read", expiresInDays = 1 })).EnsureSuccessStatusCode();

        var next = await CreateAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, next.StatusCode);
        Assert.Contains("at most 20", await next.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Tokens_belong_to_their_owner_and_only_the_owner_sees_or_revokes_them()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var (other, _, _) = await NewUserAsync(factory);
        var id = (await ReadAsync(await CreateAsync(owner))).GetProperty("id").GetGuid();

        Assert.Equal("[]", await other.GetStringAsync("/api/me/tokens"));
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/me/tokens/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/me/tokens/{id}")).StatusCode);
        Assert.Equal("[]", await owner.GetStringAsync("/api/me/tokens"));
    }

    [Fact]
    public async Task Managing_tokens_needs_a_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateApiClient().GetAsync("/api/me/tokens")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateApiClient().PostAsJsonAsync("/api/me/tokens", new { name = "x" })).StatusCode);
    }

    [Fact]
    public async Task A_token_opens_the_mcp_endpoint_but_nothing_else_so_it_cannot_make_more_tokens_or_use_the_api()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner);
        var secret = (await ReadAsync(await CreateAsync(owner))).GetProperty("token").GetString()!;
        var withToken = WithToken(factory, secret);

        Assert.Equal(HttpStatusCode.Unauthorized, (await withToken.GetAsync("/api/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await withToken.GetAsync($"/api/organizations/{org.Id}/charts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await withToken.GetAsync("/api/me/tokens")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await withToken.PostAsJsonAsync("/api/me/tokens", new { name = "more", scope = "Write", expiresInDays = 30 })).StatusCode);
    }

    [Fact]
    public async Task A_sign_in_does_not_open_the_mcp_endpoint()
    {
        var (owner, _, _) = await NewUserAsync(factory);

        var response = await owner.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("dbp_short")]
    [InlineData("dbp_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // the right shape, never issued
    [InlineData("not-a-token")]
    public async Task A_wrong_token_does_not_open_the_mcp_endpoint(string token)
    {
        var client = factory.CreateApiClient();
        if (token.Length > 0) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Last_use_is_noted_when_a_token_is_used()
    {
        var (owner, _, _) = await NewUserAsync(factory);
        var created = await ReadAsync(await CreateAsync(owner));
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(await owner.GetAsync("/api/me/tokens")))[0].GetProperty("lastUsedUtc").ValueKind);

        await WithToken(factory, created.GetProperty("token").GetString()!).PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.NotEqual(JsonValueKind.Null, (await ReadAsync(await owner.GetAsync("/api/me/tokens")))[0].GetProperty("lastUsedUtc").ValueKind);
    }
}
