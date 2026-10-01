using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;

namespace DataBoard.Server.Infrastructure;

/// <summary>
/// The GitHub OAuth app that signs people in. Both values are needed; with either missing, "Continue with GitHub" is not
/// offered and its endpoints do not exist. The app's callback URL at GitHub is <c>https://your-host/api/auth/github/callback</c>.
/// </summary>
public sealed class GitHubOptions
{
    public const string SectionName = "Authentication:GitHub";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Signing in with GitHub, using the OAuth handler that ships with ASP.NET (no extra package). GitHub is asked who the
/// person is; the answer is kept for a few minutes in a short-lived cookie (<see cref="ExternalScheme"/>) just long enough
/// for <c>AuthController</c> to turn it into an ordinary DataBoard sign-in, and then it is thrown away.
/// </summary>
internal static class GitHubSignIn
{
    public const string Scheme = "GitHub";
    public const string ExternalScheme = "External";
    public const string CallbackPath = "/api/auth/github/callback";
    public const string EmailClaim = "urn:github:email";
    public const string NameClaim = "urn:github:name";
    public const string LoginClaim = "urn:github:login";

    public static AuthenticationBuilder AddGitHubSignIn(this AuthenticationBuilder authentication)
    {
        authentication.Services.AddOptions<GitHubOptions>().BindConfiguration(GitHubOptions.SectionName);

        authentication.AddCookie(ExternalScheme, options =>
        {
            options.Cookie.Name = "databoard_external";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax; // it is read when GitHub sends the person back
            options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        });

        // Always registered: the handler refuses to exist without a client id, and settings can arrive after startup (tests do).
        // While GitHub is not set up the placeholders are never used, because the endpoints that start a sign-in answer 404.
        authentication.AddOAuth(Scheme, _ => { });
        authentication.Services.AddOptions<OAuthOptions>(Scheme).Configure<IOptions<GitHubOptions>>((options, github) =>
        {
            options.ClientId = github.Value.Enabled ? github.Value.ClientId! : "not-configured";
            options.ClientSecret = github.Value.Enabled ? github.Value.ClientSecret! : "not-configured";
            options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
            options.TokenEndpoint = "https://github.com/login/oauth/access_token";
            options.UserInformationEndpoint = "https://api.github.com/user";
            options.CallbackPath = CallbackPath;
            options.SignInScheme = ExternalScheme;
            options.Scope.Add("read:user");
            options.Scope.Add("user:email");
            options.SaveTokens = false;
            options.UsePkce = true; // a stolen code is useless without the secret only this browser's request knows

            options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
            options.ClaimActions.MapJsonKey(LoginClaim, "login");
            options.ClaimActions.MapJsonKey(NameClaim, "name");

            options.Events = new OAuthEvents
            {
                OnCreatingTicket = LoadProfileAsync,
                // Declined at GitHub, a forged or expired state, a failed token exchange: all end at the sign-in page.
                OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/login?error=github");
                    context.HandleResponse();
                    return Task.CompletedTask;
                },
            };
        });
        return authentication;
    }

    /// <summary>Reads the profile and the verified primary email. GitHub keeps the email apart from the profile.</summary>
    private static async Task LoadProfileAsync(OAuthCreatingTicketContext context)
    {
        var ct = context.HttpContext.RequestAborted;

        using (var profile = await GetAsync(context, context.Options.UserInformationEndpoint, ct))
            context.RunClaimActions(profile.RootElement);

        using var emails = await GetAsync(context, context.Options.UserInformationEndpoint + "/emails", ct);
        if (emails.RootElement.ValueKind != JsonValueKind.Array) return;

        // Only an address GitHub has checked, and only the one the person made primary, is a name for this person.
        foreach (var entry in emails.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True
                && entry.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True
                && entry.TryGetProperty("email", out var email) && email.GetString() is { Length: > 0 } address)
            {
                context.Identity?.AddClaim(new Claim(EmailClaim, address));
                return;
            }
        }
    }

    private static async Task<JsonDocument> GetAsync(OAuthCreatingTicketContext context, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("DataBoard"); // GitHub's API refuses requests without one
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

        using var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
}
