using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DataBoard.Server.IntegrationTests;

internal static class ApiHelpers
{
    public const string Password = "correct-horse-battery";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public sealed record Session(string AccessToken, UserDto User);
    public sealed record UserDto(Guid Id, string Email, string DisplayName);
    public sealed record OrgDto(Guid Id, string Name, string Role);
    public sealed record DataSourceDto(Guid Id, Guid OrganizationId, string Name, string Type, DateTime CreatedUtc);

    public static async Task<string> RegisterAsync(HttpClient client, string? email = null, string password = Password)
    {
        email ??= NewEmail();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password, displayName = "Test User" });
        response.EnsureSuccessStatusCode();
        return email;
    }

    public static async Task<(HttpResponseMessage Response, Session? Session)> LoginAsync(
        HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var session = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<Session>(Json) : null;
        return (response, session);
    }

    /// <summary>Registers and logs in a new user; returns a client carrying that user's bearer token.</summary>
    public static async Task<(HttpClient Client, string Email, UserDto User)> NewUserAsync(ApiFactory factory)
    {
        var client = factory.CreateApiClient();
        var email = await RegisterAsync(client);
        var (_, session) = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);
        return (client, email, session.User);
    }

    public static async Task<OrgDto> CreateOrgAsync(HttpClient client, string name = "Acme")
    {
        var response = await client.PostAsJsonAsync("/api/organizations", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrgDto>(Json))!;
    }

    /// <summary>Extracts "name=value" of the refresh cookie from a Set-Cookie header, for manual replay.</summary>
    public static string? RefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        var cookie = values.FirstOrDefault(v => v.StartsWith("databoard_refresh=", StringComparison.Ordinal));
        return cookie?.Split(';')[0];
    }

    public static HttpRequestMessage RefreshRequest(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return request;
    }
}
