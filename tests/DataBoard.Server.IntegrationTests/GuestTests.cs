using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataBoard.Application.Identity;
using DataBoard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>"Continue as guest": a temporary account with a sandbox of its own, which cannot reach outside it.</summary>
[Collection(ApiCollection.Name)]
public class GuestTests(ApiFactory factory)
{
    private sealed record Org(Guid Id, string Name, string Role, bool IsDefault);

    private WebApplicationFactory<Program> WithGuests(bool enabled = true, int maxActive = 200) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Guests:Enabled"] = enabled.ToString(),
                ["Guests:MaxActive"] = maxActive.ToString(),
                ["RateLimiting:GuestPermitLimit"] = "1000",
            })));

    private static async Task<(HttpClient Client, Session Session)> StartAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var response = await client.PostAsync("/api/auth/guest", null);
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<Session>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return (client, session);
    }

    [Fact]
    public async Task Guests_are_off_unless_turned_on()
    {
        using var app = WithGuests(enabled: false);
        var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/auth/guest", null)).StatusCode);
        Assert.Contains("\"guest\":false", await client.GetStringAsync("/api/auth/providers"));
    }

    [Fact]
    public async Task A_guest_gets_a_sandbox_of_their_own_and_never_sees_the_default_organization()
    {
        using var app = WithGuests();
        Assert.Contains("\"guest\":true", await app.CreateClient().GetStringAsync("/api/auth/providers"));

        var (client, session) = await StartAsync(app);
        var orgs = (await client.GetFromJsonAsync<List<Org>>("/api/organizations", Json))!;

        var sandbox = Assert.Single(orgs);
        Assert.False(sandbox.IsDefault);
        Assert.Equal("Owner", sandbox.Role);
        Assert.StartsWith("guest-", session.User.Email);
    }

    [Fact]
    public async Task A_guest_cannot_reach_outside_the_sandbox()
    {
        using var app = WithGuests();
        var (client, _) = await StartAsync(app);
        var org = (await client.GetFromJsonAsync<List<Org>>("/api/organizations", Json))!.Single();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/organizations", new { name = "More" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/me/tokens", new { name = "t", scope = "Read", neverExpires = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/organizations/{org.Id}/data-sources",
            new { organizationId = org.Id, name = "Mine", type = "PostgreSql", connectionString = "Host=example.com;Database=x;Username=u;Password=p" })).StatusCode);
    }

    [Fact]
    public async Task Only_so_many_guests_can_be_here_at_once()
    {
        using var app = WithGuests(maxActive: 0);

        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient().PostAsync("/api/auth/guest", null)).StatusCode);
    }

    [Fact]
    public async Task An_expired_guest_is_deleted_with_their_sandbox()
    {
        using var app = WithGuests();
        var (client, session) = await StartAsync(app);
        var org = (await client.GetFromJsonAsync<List<Org>>("/api/organizations", Json))!.Single();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE users SET \"GuestExpiresUtc\" = now() - interval '1 hour' WHERE \"Id\" = {0}", session.User.Id);
            Assert.True(await scope.ServiceProvider.GetRequiredService<IIdentityService>().DeleteExpiredGuestsAsync() >= 1);

            Assert.Null(await scope.ServiceProvider.GetRequiredService<IIdentityService>().GetUserAsync(session.User.Id));
            Assert.False(await db.Organizations.AnyAsync(o => o.Id == org.Id));
        }
    }
}
