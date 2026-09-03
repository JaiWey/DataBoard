using DataBoard.Application.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataBoard.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations")]
public class OrganizationsController : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<OrganizationDto>>> ListMine(
		[FromServices] ListMyOrganizationsHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(ct));

	[HttpPost]
	public async Task<ActionResult<OrganizationDto>> Create(
		CreateOrganizationRequest body, [FromServices] CreateOrganizationHandler handler, CancellationToken ct)
	{
		var created = await handler.HandleAsync(body, ct);
		return Created($"api/organizations/{created.Id}", created);
	}

	[HttpPost("{organizationId:guid}/members")]
	public async Task<IActionResult> AddMember(
		Guid organizationId, AddMemberRequest body, [FromServices] AddMemberHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, body, ct);
		return NoContent();
	}
}
