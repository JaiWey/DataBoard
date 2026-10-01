using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>
/// Signing in with GitHub. GitHub itself is played by a fake the server's OAuth handler talks to, so the whole round trip
/// (to GitHub, the callback, the sign-in, the session) runs for real.
/// </summary>
[Collection(ApiCollection.Name)]
public class GitHubSignInTests(ApiFactory factory)
{
    /// <summary>What "GitHub" says about the person who is signing in.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private static long _next = 100_000;

        /// <summary>GitHub's id for the person; every fake is someone new unless a test says otherwise.</summary>
        public long Id { get; set; } = Interlocked.Increment(ref _next);
        public string Login { get; set; } = "octocat";
        public string? Name { get; set; } = "Octo Cat";
        public string Emails { get; set; } = "[]";
        public bool Declines { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsoluteUri;
            if (path == "https://github.com/login/oauth/access_token")
                return Task.FromResult(Declines ? Json("""{"error":"bad_verification_code"}""") : Json("""{"access_token":"gho_test","token_type":"bearer","scope":"read:user,user:email"}"""));
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); // GitHub's API needs the token and a User-Agent
            Assert.NotEmpty(request.Headers.UserAgent);
            if (path == "https://api.github.com/user")
                return Task.FromResult(Json(JsonSerializer.Serialize(new { id = Id, login = Login, name = Name })));
            if (path == "https://api.github.com/user/emails") return Task.FromResult(Json(Emails));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public static string Verified(string email) => JsonSerializer.Serialize(new[]
        {
            new { email = "noreply@users.example", primary = false, verified = true },
            new { email, primary = true, verified = true },
        });
    }

    private WebApplicationFactory<Program> WithGitHub(FakeGitHub github, bool configured = true) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:GitHub:ClientId"] = configured ? "test-client-id" : null,
                ["Authentication:GitHub:ClientSecret"] = configured ? "test-client-secret" : null,
            }));
            builder.ConfigureServices(services => services.Configure<OAuthOptions>("GitHub", o => o.BackchannelHttpHandler = github));
        });

    /// <summary>A browser: it keeps cookies and follows nothing by itself, so each step can be looked at.</summary>
    private static HttpClient Browser(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private sealed record Landed(string Location, Session? Session);

    /// <summary>The whole trip: start, GitHub sends the person back with a code, finish, and ask for the session.</summary>
    private static async Task<Landed> SignInAsync(HttpClient browser, string? returnUrl = null, string? code = "the-code")
    {
        var start = await browser.GetAsync("/api/auth/github" + (returnUrl is null ? "" : "?returnUrl=" + Uri.EscapeDataString(returnUrl)));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var state = HttpUtility.ParseQueryString(start.Headers.Location!.Query)["state"]!;

        var back = await browser.GetAsync($"/api/auth/github/callback?code={code}&state={Uri.EscapeDataString(state)}");
        if (back.StatusCode != HttpStatusCode.Redirect || back.Headers.Location!.OriginalString.StartsWith("/login", StringComparison.Ordinal))
            return new Landed(back.Headers.Location?.OriginalString ?? back.StatusCode.ToString(), null);

        var done = await browser.GetAsync(back.Headers.Location!);
        var location = done.Headers.Location!.OriginalString;
        var refreshed = await browser.PostAsync("/api/auth/refresh", null);
        return new Landed(location, refreshed.IsSuccessStatusCode ? await refreshed.Content.ReadFromJsonAsync<Session>(Json) : null);
    }

    [Fact]
    public async Task The_sign_in_page_is_told_whether_github_is_set_up()
    {
        var plain = await factory.CreateApiClient().GetFromJsonAsync<JsonElement>("/api/auth/providers");
        Assert.False(plain.GetProperty("gitHub").GetBoolean());

        var configured = await Browser(WithGitHub(new FakeGitHub())).GetFromJsonAsync<JsonElement>("/api/auth/providers");
        Assert.True(configured.GetProperty("gitHub").GetBoolean());
    }

    [Fact]
    public async Task Without_a_client_id_and_secret_there_is_nothing_to_start()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateApiClient().GetAsync("/api/auth/github")).StatusCode);

        var unconfigured = Browser(WithGitHub(new FakeGitHub(), configured: false));
        Assert.Equal(HttpStatusCode.NotFound, (await unconfigured.GetAsync("/api/auth/github")).StatusCode);
        var callback = await unconfigured.GetAsync("/api/auth/github/callback?code=x&state=y");
        Assert.Equal("/login?error=github", callback.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Starting_sends_the_browser_to_github_with_what_it_needs_to_come_back_safely()
    {
        var start = await Browser(WithGitHub(new FakeGitHub())).GetAsync("/api/auth/github");

        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var url = start.Headers.Location!;
        Assert.Equal("https://github.com/login/oauth/authorize", url.GetLeftPart(UriPartial.Path));
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("test-client-id", query["client_id"]);
        Assert.Equal("https://localhost/api/auth/github/callback", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("read:user user:email", query["scope"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.DoesNotContain("test-client-secret", url.OriginalString);
    }

    [Fact]
    public async Task A_new_person_gets_an_account_made_from_their_verified_primary_email_and_is_signed_in()
    {
        var email = NewEmail();
        var github = new FakeGitHub { Emails = FakeGitHub.Verified(email) };
        var app = WithGitHub(github);

        var landed = await SignInAsync(Browser(app));

        Assert.Equal("/", landed.Location);
        Assert.Equal(email, landed.Session!.User.Email);
        Assert.Equal("Octo Cat", landed.Session.User.DisplayName);

        var api = app.CreateClient();
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", landed.Session.AccessToken);
        Assert.Equal(email, (await api.GetFromJsonAsync<UserDto>("/api/auth/me", Json))!.Email);
        Assert.Equal(HttpStatusCode.Created, (await api.PostAsJsonAsync("/api/organizations", new { name = "From GitHub" })).StatusCode);
    }

    [Fact]
    public async Task Coming_back_signs_in_the_same_account_even_when_the_email_at_github_has_changed()
    {
        var github = new FakeGitHub { Id = 777, Emails = FakeGitHub.Verified(NewEmail()) };
        var app = WithGitHub(github);
        var first = await SignInAsync(Browser(app));

        github.Emails = FakeGitHub.Verified(NewEmail());
        github.Name = "A New Name";
        var second = await SignInAsync(Browser(app));

        Assert.Equal(first.Session!.User.Id, second.Session!.User.Id);
        Assert.Equal(first.Session.User.Email, second.Session.User.Email);
    }

    [Fact]
    public async Task A_name_missing_at_github_falls_back_to_the_login_and_a_long_one_is_cut()
    {
        var github = new FakeGitHub { Name = null, Login = "just-a-login", Emails = FakeGitHub.Verified(NewEmail()) };
        Assert.Equal("just-a-login", (await SignInAsync(Browser(WithGitHub(github)))).Session!.User.DisplayName);

        github.Id = 99;
        github.Name = new string('x', 300);
        github.Emails = FakeGitHub.Verified(NewEmail());
        Assert.Equal(100, (await SignInAsync(Browser(WithGitHub(github)))).Session!.User.DisplayName.Length);
    }

    [Fact]
    public async Task A_verified_email_that_belongs_to_an_account_links_to_it_and_keeps_its_organizations()
    {
        var (owner, email, user) = await NewUserAsync(factory);
        var org = await CreateOrgAsync(owner, "Mine");
        var github = new FakeGitHub { Id = 31337, Emails = FakeGitHub.Verified(email) };

        var landed = await SignInAsync(Browser(WithGitHub(github)));

        Assert.Equal(user.Id, landed.Session!.User.Id);
        var api = factory.CreateApiClient();
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", landed.Session.AccessToken);
        var orgs = await api.GetFromJsonAsync<JsonElement>("/api/organizations");
        Assert.Contains(orgs.EnumerateArray(), o => o.GetProperty("id").GetGuid() == org.Id);
    }

    [Fact]
    public async Task Linking_an_account_whose_email_was_never_checked_takes_the_password_and_the_other_sessions_away()
    {
        // Registering does not check the email, so someone could have made the account in the hope its owner turns up.
        var squatter = factory.CreateApiClient();
        var email = await RegisterAsync(squatter);
        var (_, before) = await LoginAsync(squatter, email);
        var staleCookie = RefreshCookie((await squatter.PostAsJsonAsync("/api/auth/login", new { email, password = Password })))!;

        await SignInAsync(Browser(WithGitHub(new FakeGitHub { Id = 555, Emails = FakeGitHub.Verified(email) })));

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(squatter, email)).Response.StatusCode);
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replay.Headers.Add("Cookie", staleCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await squatter.SendAsync(replay)).StatusCode);
        Assert.NotNull(before);
    }

    [Fact]
    public async Task An_account_made_with_github_has_no_password_to_guess()
    {
        var email = NewEmail();
        await SignInAsync(Browser(WithGitHub(new FakeGitHub { Id = 808, Emails = FakeGitHub.Verified(email) })));

        foreach (var password in new[] { Password, "", "anything" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(factory.CreateApiClient(), email, password)).Response.StatusCode);
    }

    [Fact]
    public async Task Without_a_verified_primary_email_nobody_is_signed_in_and_nothing_is_made()
    {
        var emails = new[]
        {
            "[]",
            JsonSerializer.Serialize(new[] { new { email = "a@example.com", primary = true, verified = false } }),
            JsonSerializer.Serialize(new[] { new { email = "b@example.com", primary = false, verified = true } }),
        };
        var id = 9000;
        foreach (var body in emails)
        {
            var landed = await SignInAsync(Browser(WithGitHub(new FakeGitHub { Id = id++, Emails = body })));

            Assert.Equal("/login?error=github-email", landed.Location);
            Assert.Null(landed.Session);
        }
        Assert.Null((await LoginAsync(factory.CreateApiClient(), "a@example.com")).Session);
    }

    [Fact]
    public async Task A_person_who_declines_or_a_forged_state_ends_at_the_sign_in_page_with_no_session()
    {
        var app = WithGitHub(new FakeGitHub { Declines = true, Emails = FakeGitHub.Verified(NewEmail()) });
        var declined = await SignInAsync(Browser(app));
        Assert.Equal("/login?error=github", declined.Location);
        Assert.Null(declined.Session);

        var browser = Browser(WithGitHub(new FakeGitHub()));
        await browser.GetAsync("/api/auth/github");
        var forged = await browser.GetAsync("/api/auth/github/callback?code=x&state=forged");
        Assert.Equal("/login?error=github", forged.Headers.Location?.OriginalString);

        var error = await browser.GetAsync("/api/auth/github/callback?error=access_denied");
        Assert.Equal("/login?error=github", error.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Finishing_without_going_through_github_signs_nobody_in()
    {
        var browser = Browser(WithGitHub(new FakeGitHub()));

        var response = await browser.GetAsync("/api/auth/github/complete");

        Assert.Equal("/login?error=github", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Theory]
    [InlineData("/organizations/123/dashboards", "/organizations/123/dashboards")]
    [InlineData("//evil.example/x", "/")]
    [InlineData("https://evil.example/x", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("evil", "/")]
    public async Task Only_a_page_of_this_site_can_be_gone_back_to(string returnUrl, string expected)
    {
        var github = new FakeGitHub { Id = 1234 + returnUrl.Length, Emails = FakeGitHub.Verified(NewEmail()) };

        var landed = await SignInAsync(Browser(WithGitHub(github)), returnUrl);

        Assert.Equal(expected, landed.Location);
    }

    [Fact]
    public async Task The_session_it_makes_is_an_ordinary_one_with_the_refresh_cookie_kept_from_scripts()
    {
        var github = new FakeGitHub { Id = 6060, Emails = FakeGitHub.Verified(NewEmail()) };
        var app = WithGitHub(github);
        var browser = Browser(app);
        var start = await browser.GetAsync("/api/auth/github");
        var state = HttpUtility.ParseQueryString(start.Headers.Location!.Query)["state"]!;
        var back = await browser.GetAsync($"/api/auth/github/callback?code=c&state={Uri.EscapeDataString(state)}");

        var done = await browser.GetAsync(back.Headers.Location!);

        var cookies = string.Join(";", done.Headers.GetValues("Set-Cookie"));
        Assert.Contains("databoard_refresh=", cookies);
        Assert.Contains("httponly", cookies, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookies, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gho_test", cookies); // GitHub's token is not kept
        Assert.Contains("databoard_external=;", cookies); // and the short-lived answer is cleared
    }
}
