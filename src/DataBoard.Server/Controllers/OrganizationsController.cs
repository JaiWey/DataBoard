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

	[HttpGet("{organizationId:guid}/members")]
	public async Task<ActionResult<IReadOnlyList<OrganizationMemberDto>>> ListMembers(
		Guid organizationId, [FromServices] ListMembersHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, ct));

	[HttpPut("{organizationId:guid}/members/{userId:guid}")]
	public async Task<IActionResult> ChangeMemberRole(
		Guid organizationId, Guid userId, ChangeMemberRoleRequest body, [FromServices] ChangeMemberRoleHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, userId, body, ct);
		return NoContent();
	}

	[HttpDelete("{organizationId:guid}/members/{userId:guid}")]
	public async Task<IActionResult> RemoveMember(
		Guid organizationId, Guid userId, [FromServices] RemoveMemberHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, userId, ct);
		return NoContent();
	}

	[HttpPost("{organizationId:guid}/members")]
	public async Task<IActionResult> AddMember(
		Guid organizationId, AddMemberRequest body, [FromServices] AddMemberHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, body, ct);
		return NoContent();
	}
}
