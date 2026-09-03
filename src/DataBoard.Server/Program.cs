using System.Threading.RateLimiting;
using DataBoard.Application;
using DataBoard.Application.Abstractions;
using DataBoard.Infrastructure;
using DataBoard.Infrastructure.Identity;
using DataBoard.Infrastructure.Persistence;
using DataBoard.Server.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
	.AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

// Options are read lazily so settings supplied after startup (for example by tests) are honoured.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
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

if (app.Environment.IsDevelopment())
{
	// Convenience for local development only; production migrations should be an explicit deployment step.
	using var scope = app.Services.CreateScope();
	await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
	await DevelopmentSeeder.SeedAsync(scope.ServiceProvider, app.Logger);
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

app.MapFallbackToFile("/index.html");

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program;
