using DataBoard.Application.Dashboards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataBoard.Server.Controllers;

/// <summary>
/// Who may see a dashboard or a dashboard folder (and everything in a folder). Editors and Owners who can see the thing
/// themselves may read and set it; Owners of the organization always see everything.
/// </summary>
[ApiController]
[Authorize]
public class AccessController : ControllerBase
{
	[HttpGet("api/organizations/{organizationId:guid}/dashboards/{dashboardId:guid}/access")]
	public async Task<ActionResult<AccessDto>> GetDashboard(
		Guid organizationId, Guid dashboardId, [FromServices] GetDashboardAccessHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, ct));

	[HttpPut("api/organizations/{organizationId:guid}/dashboards/{dashboardId:guid}/access")]
	public async Task<ActionResult<AccessDto>> SetDashboard(
		Guid organizationId, Guid dashboardId, SetAccessRequest? body, [FromServices] SetDashboardAccessHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, body, ct));

	[HttpGet("api/organizations/{organizationId:guid}/folders/{folderId:guid}/access")]
	public async Task<ActionResult<AccessDto>> GetFolder(
		Guid organizationId, Guid folderId, [FromServices] GetFolderAccessHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, folderId, ct));

	[HttpPut("api/organizations/{organizationId:guid}/folders/{folderId:guid}/access")]
	public async Task<ActionResult<AccessDto>> SetFolder(
		Guid organizationId, Guid folderId, SetAccessRequest? body, [FromServices] SetFolderAccessHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, folderId, body, ct));
}
