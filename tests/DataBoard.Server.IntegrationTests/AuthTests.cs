using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_is_public()
    {
        var response = await factory.CreateApiClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_login_and_me_work()
    {
        var client = factory.CreateApiClient();
        var email = await RegisterAsync(client);

        var (response, session) = await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(email, session!.User.Email);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me", Json);
        Assert.Equal(session.User.Id, me!.Id);
    }

    [Fact]
    public async Task Login_response_never_contains_the_refresh_token_but_sets_an_httponly_cookie()
    {
        var client = factory.CreateApiClient();
        var email = await RegisterAsync(client);

        var (response, _) = await LoginAsync(client, email);
        var body = await response.Content.ReadAsStringAsync();
        var setCookie = string.Join(";", response.Headers.GetValues("Set-Cookie"));

        Assert.DoesNotContain("refresh", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("databoard_refresh=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Protected_endpoints_reject_anonymous_and_garbage_tokens()
    {
        var client = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/organizations")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/organizations")).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_401()
    {
        var client = factory.CreateApiClient();
        var email = await RegisterAsync(client);

        var wrong = await LoginAsync(client, email, "wrong-password-123");
        var unknown = await LoginAsync(client, NewEmail());

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Response.StatusCode);
        Assert.Equal(
            await wrong.Response.Content.ReadAsStringAsync().ContinueWith(t => Detail(t.Result)),
            await unknown.Response.Content.ReadAsStringAsync().ContinueWith(t => Detail(t.Result)));
    }

    [Fact]
    public async Task Weak_password_and_duplicate_email_are_validation_errors()
    {
        var client = factory.CreateApiClient();

        var weak = await client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "short", displayName = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("Password", await weak.Content.ReadAsStringAsync());

        var email = await RegisterAsync(client);
        var duplicate = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = Password, displayName = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var client = factory.CreateApiClient();
        var email = await RegisterAsync(client);

        for (var i = 0; i < 5; i++)
            await LoginAsync(client, email, "wrong-password-123");

        var (response, _) = await LoginAsync(client, email, Password);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reuse_of_the_old_one_revokes_everything()
    {
        var client = factory.CreateApiClient();
        var email = await RegisterAsync(client);
        var (login, _) = await LoginAsync(client, email);
        var first = RefreshCookie(login)!;

        var refreshed = await client.SendAsync(RefreshRequest(first));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = RefreshCookie(refreshed)!;
        Assert.NotEqual(first, second);

        // Replaying the already-used token is treated as theft...
        var replay = await client.SendAsync(RefreshRequest(first));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // ...so the newer token is dead too.
        var afterReplay = await client.SendAsync(RefreshRequest(second));
        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var client = factory.CreateApiClient();
        var email = await RegisterAsync(client);
        var (login, _) = await LoginAsync(client, email);
        var cookie = RefreshCookie(login)!;

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(RefreshRequest(cookie))).StatusCode);
    }

    [Fact]
    public async Task Refresh_without_a_cookie_is_401()
    {
        var response = await factory.CreateApiClient().PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string? Detail(string json) =>
        JsonDocument.Parse(json).RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
}
