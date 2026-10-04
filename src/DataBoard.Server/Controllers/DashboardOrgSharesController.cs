using DataBoard.Application.Dashboards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataBoard.Server.Controllers;

/// <summary>
/// Dashboards shown to other organizations. The owning organization decides (Editors and Owners, with organizations they
/// belong to); the receiving organization's members see the dashboard read-only, live, as the same page a public link shows.
/// </summary>
[ApiController]
[Authorize]
public class DashboardOrgSharesController : ControllerBase
{
	// ---- the owning side ----

	[HttpGet("api/organizations/{organizationId:guid}/dashboards/{dashboardId:guid}/organization-shares")]
	public async Task<ActionResult<IReadOnlyList<OrgShareDto>>> List(
		Guid organizationId, Guid dashboardId, [FromServices] ListOrgSharesHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, ct));

	[HttpPost("api/organizations/{organizationId:guid}/dashboards/{dashboardId:guid}/organization-shares")]
	public async Task<ActionResult<OrgShareDto>> Share(
		Guid organizationId, Guid dashboardId, ShareWithOrganizationRequest body, [FromServices] ShareWithOrganizationHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, body, ct));

	[HttpDelete("api/organizations/{organizationId:guid}/dashboards/{dashboardId:guid}/organization-shares/{targetOrganizationId:guid}")]
	public async Task<IActionResult> Unshare(
		Guid organizationId, Guid dashboardId, Guid targetOrganizationId, [FromServices] UnshareWithOrganizationHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, dashboardId, targetOrganizationId, ct);
		return NoContent();
	}

	// ---- the receiving side: {organizationId} is the organization that is looking ----

	[HttpGet("api/organizations/{organizationId:guid}/shared-dashboards")]
	public async Task<ActionResult<IReadOnlyList<SharedWithMeDto>>> SharedWithMe(
		Guid organizationId, [FromServices] ListSharedWithMeHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, ct));

	[HttpGet("api/organizations/{organizationId:guid}/shared-dashboards/{sourceOrganizationId:guid}/{dashboardId:guid}")]
	public async Task<ActionResult<SharedDashboardDto>> Open(
		Guid organizationId, Guid sourceOrganizationId, Guid dashboardId, [FromServices] GetOrgSharedDashboardHandler handler, CancellationToken ct)
	{
		NoStore();
		return Ok(await handler.HandleAsync(organizationId, sourceOrganizationId, dashboardId, ct));
	}

	[HttpPost("api/organizations/{organizationId:guid}/shared-dashboards/{sourceOrganizationId:guid}/{dashboardId:guid}/run")]
	public async Task<ActionResult<DashboardRunResult>> Run(
		Guid organizationId, Guid sourceOrganizationId, Guid dashboardId, RunDashboardRequest? body,
		[FromServices] RunOrgSharedDashboardHandler handler, CancellationToken ct)
	{
		NoStore();
		return Ok(await handler.HandleAsync(organizationId, sourceOrganizationId, dashboardId, body, ct));
	}

	[HttpGet("api/organizations/{organizationId:guid}/shared-dashboards/{sourceOrganizationId:guid}/{dashboardId:guid}/background")]
	public async Task<IActionResult> Background(
		Guid organizationId, Guid sourceOrganizationId, Guid dashboardId, [FromServices] GetOrgSharedBackgroundHandler handler, CancellationToken ct)
	{
		var image = await handler.HandleAsync(organizationId, sourceOrganizationId, dashboardId, ct);
		return Picture(image);
	}

	[HttpGet("api/organizations/{organizationId:guid}/shared-dashboards/{sourceOrganizationId:guid}/{dashboardId:guid}/images/{widgetId:guid}")]
	public async Task<IActionResult> Image(
		Guid organizationId, Guid sourceOrganizationId, Guid dashboardId, Guid widgetId, [FromServices] GetOrgSharedImageWidgetHandler handler, CancellationToken ct)
	{
		var image = await handler.HandleAsync(organizationId, sourceOrganizationId, dashboardId, widgetId, ct);
		return Picture(image);
	}

	private IActionResult Picture(DataBoard.Application.Dashboards.BackgroundImage image)
	{
		Response.Headers.CacheControl = "private, max-age=300";
		Response.Headers["X-Content-Type-Options"] = "nosniff";
		return File(image.Content, image.ContentType);
	}

	/// <summary>The data belongs to the people it was shared with, so no shared cache may keep it.</summary>
	private void NoStore() => Response.Headers.CacheControl = "no-store";
}
