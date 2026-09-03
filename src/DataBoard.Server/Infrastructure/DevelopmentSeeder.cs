using DataBoard.Application.Abstractions;
using DataBoard.Application.Identity;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Organizations;

namespace DataBoard.Server.Infrastructure;

public sealed class SeedOptions
{
	public const string SectionName = "Seed";

	public bool Enabled { get; set; }
	public string Email { get; set; } = string.Empty;
	public string Password { get; set; } = string.Empty;
	public string DisplayName { get; set; } = "Dev User";
	public string OrganizationName { get; set; } = "Demo Org";
}

/// <summary>
/// Creates a local sign-in so the app is usable right after the first run. Idempotent. The caller must only
/// invoke it in Development, and the credentials come from configuration, never from production settings.
/// </summary>
public static class DevelopmentSeeder
{
	public static async Task SeedAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
	{
		var options = services.GetRequiredService<IConfiguration>().GetSection(SeedOptions.SectionName).Get<SeedOptions>();
		if (options is not { Enabled: true } || options.Email.Length == 0 || options.Password.Length == 0)
			return;

		var identity = services.GetRequiredService<IIdentityService>();
		var organizations = services.GetRequiredService<IOrganizationRepository>();
		var unitOfWork = services.GetRequiredService<IUnitOfWork>();
		var clock = services.GetRequiredService<IClock>();

		var userId = await identity.FindUserIdByEmailAsync(options.Email, ct);
		if (userId is null)
		{
			var user = await identity.RegisterAsync(options.Email, options.Password, options.DisplayName, ct);
			userId = user.Id;
			logger.LogWarning("Seeded development user {Email}. Never enable seeding outside local development.", options.Email);
		}

		if ((await organizations.ListForUserAsync(userId.Value, ct)).Count == 0)
		{
			var now = clock.UtcNow;
			var org = Organization.Create(options.OrganizationName, now);
			await organizations.AddAsync(org, OrganizationMember.Create(org.Id, userId.Value, OrganizationRole.Owner, now), ct);
			await unitOfWork.SaveChangesAsync(ct);
		}
	}
}
