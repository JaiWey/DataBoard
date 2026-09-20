using DataBoard.Application.Folders;
using DataBoard.Domain.Folders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataBoard.Server.Controllers;

/// <summary>
/// Folders that organize an organization's datasets, charts and dashboards (each kind has its own tree). Any member can
/// list them; Editors and Owners change them.
/// </summary>
[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}/folders")]
public class FoldersController : ControllerBase
{
	/// <summary>The folders of one kind: Dataset (the default), Chart or Dashboard.</summary>
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<FolderDto>>> List(
		Guid organizationId, [FromQuery] FolderKind kind, [FromServices] ListFoldersHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, kind == 0 ? FolderKind.Dataset : kind, ct));

	[HttpPost]
	public async Task<ActionResult<FolderDto>> Create(
		Guid organizationId, CreateFolderRequest body, [FromServices] CreateFolderHandler handler, CancellationToken ct)
	{
		var created = await handler.HandleAsync(organizationId, body, ct);
		return Created($"api/organizations/{organizationId}/folders/{created.Id}", created);
	}

	/// <summary>Renames the folder and/or moves it under another one (parentId null is the top level).</summary>
	[HttpPut("{folderId:guid}")]
	public async Task<ActionResult<FolderDto>> Update(
		Guid organizationId, Guid folderId, UpdateFolderRequest body, [FromServices] UpdateFolderHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, folderId, body, ct));

	/// <summary>Only an empty folder can be deleted.</summary>
	[HttpDelete("{folderId:guid}")]
	public async Task<IActionResult> Delete(
		Guid organizationId, Guid folderId, [FromServices] DeleteFolderHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, folderId, ct);
		return NoContent();
	}
}
