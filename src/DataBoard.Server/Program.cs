using System.Threading.RateLimiting;
using DataBoard.Application;
using DataBoard.Application.Abstractions;
using DataBoard.Infrastructure;
using DataBoard.Infrastructure.Identity;
using DataBoard.Infrastructure.Persistence;
using DataBoard.Server.Infrastructure;
using DataBoard.Server.Mcp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
	.AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// The keys that protect stored secrets (data source passwords, share links) are kept in this folder when one is named, so
// they survive a restart or a new container. Without it the framework picks its own place, which a container loses.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
	builder.Services.AddDataProtection()
		.PersistKeysToFileSystem(new DirectoryInfo(keysPath))
		.SetApplicationName("DataBoard");
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

// Options are read lazily so settings supplied after startup (for example by tests) are honoured.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer()
	.AddGitHubSignIn()
	// Personal access tokens are for the MCP endpoint only; the rest of the API takes a sign-in (JWT) and nothing else.
	.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(ApiTokenDefaults.Scheme, null);
builder.Services.AddDataBoardMcp();

// The scheduled refresh of dashboards runs inside the server (each server may run it; a due dashboard goes to one of them).
builder.Services.Configure<DashboardRefreshOptions>(builder.Configuration.GetSection(DashboardRefreshOptions.SectionName));
builder.Services.AddHostedService<DashboardRefreshService>();
builder.Services.AddHostedService<GuestCleanupService>(); // deletes guests whose day is up
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
	.Configure<IOptions<JwtOptions>>((bearer, jwt) =>
	{
		bearer.MapInboundClaims = false;
		bearer.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuer = true,
			ValidIssuer = jwt.Value.Issuer,
			ValidateAudience = true,
			ValidAudience = jwt.Value.Audience,
			ValidateIssuerSigningKey = true,
			IssuerSigningKey = new SymmetricSecurityKey(jwt.Value.KeyBytes()),
			ValidateLifetime = true,
			ClockSkew = TimeSpan.FromSeconds(30),
			ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
		};
	});
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	// Every call opens a connection to a user-supplied host, so limit them per signed-in user.
	options.AddPolicy("connections", context =>
	{
		var limit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:ConnectionPermitLimit", 30);
		var user = context.User.FindFirst(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Sub)?.Value
			?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		return RateLimitPartition.GetFixedWindowLimiter(
			user,
			_ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
	});
	// Public share links: nobody is signed in, and every run queries the owner's database, so limit per address.
	options.AddPolicy("shared", context =>
	{
		var limit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:SharedPermitLimit", 30);
		return RateLimitPartition.GetFixedWindowLimiter(
			context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			_ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
	});
	// The MCP endpoint is authenticated after this runs, so the caller is told apart by the token they send (never kept).
	options.AddPolicy("mcp", context =>
	{
		var limit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:McpPermitLimit", 60);
		var credential = context.Request.Headers.Authorization.ToString();
		var key = credential.Length > 0
			? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(credential)))
			: context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		return RateLimitPartition.GetFixedWindowLimiter(
			key,
			_ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
	});
	// "Continue as guest" makes an account each time, so it is limited much harder than a sign-in.
	options.AddPolicy("guest", context =>
	{
		var limit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:GuestPermitLimit", 5);
		return RateLimitPartition.GetFixedWindowLimiter(
			context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			_ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromHours(1) });
	});
	options.AddPolicy("auth", context =>
	{
		var limit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:AuthPermitLimit", 10);
		return RateLimitPartition.GetFixedWindowLimiter(
			context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			_ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
	});
});

var app = builder.Build();

app.UseExceptionHandler();

// Migrations run on start in development, and elsewhere only when asked for (Database:MigrateOnStartup=true): a deployment
// with one server can turn it on; with several, run the migrations as a step of their own. Demo data is development only.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
	using var scope = app.Services.CreateScope();
	await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
	if (app.Environment.IsDevelopment()) await DevelopmentSeeder.SeedAsync(scope.ServiceProvider, app.Logger);
}

app.UseDefaultFiles();
app.MapStaticAssets();

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseRateLimiter(); // after authentication so the "connections" policy can limit per signed-in user
app.UseAuthorization();

app.MapControllers();
app.MapDataBoardMcp();

app.MapFallbackToFile("/index.html");

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program;
