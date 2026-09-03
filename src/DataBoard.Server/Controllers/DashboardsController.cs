using DataBoard.Application.Dashboards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataBoard.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}/dashboards")]
public class DashboardsController : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<DashboardSummaryDto>>> List(
		Guid organizationId, [FromServices] ListDashboardsHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, ct));

	[HttpGet("{dashboardId:guid}")]
	public async Task<ActionResult<DashboardDto>> Get(
		Guid organizationId, Guid dashboardId, [FromServices] GetDashboardHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, ct));

	[HttpPost]
	public async Task<ActionResult<DashboardDto>> Create(
		Guid organizationId, SaveDashboardRequest body, [FromServices] CreateDashboardHandler handler, CancellationToken ct)
	{
		var created = await handler.HandleAsync(organizationId, body, ct);
		return Created($"api/organizations/{organizationId}/dashboards/{created.Id}", created);
	}

	[HttpPut("{dashboardId:guid}")]
	public async Task<ActionResult<DashboardDto>> Update(
		Guid organizationId, Guid dashboardId, SaveDashboardRequest body, [FromServices] UpdateDashboardHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, body, ct));

	[HttpDelete("{dashboardId:guid}")]
	public async Task<IActionResult> Delete(
		Guid organizationId, Guid dashboardId, [FromServices] DeleteDashboardHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, dashboardId, ct);
		return NoContent();
	}

	/// <summary>Runs every widget with the viewer's shared-filter values applied. Any member may run a dashboard.</summary>
	[HttpPost("{dashboardId:guid}/run")]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<DashboardRunResult>> Run(
		Guid organizationId, Guid dashboardId, RunDashboardRequest? body, [FromServices] RunDashboardHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, body, ct));
}
