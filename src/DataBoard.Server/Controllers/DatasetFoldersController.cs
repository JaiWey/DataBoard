using DataBoard.Application.Datasets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataBoard.Server.Controllers;

/// <summary>Folders that organize an organization's datasets. Any member can list them; Editors and Owners change them.</summary>
[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}/dataset-folders")]
public class DatasetFoldersController : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<DatasetFolderDto>>> List(
		Guid organizationId, [FromServices] ListFoldersHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, ct));

	[HttpPost]
	public async Task<ActionResult<DatasetFolderDto>> Create(
		Guid organizationId, CreateFolderRequest body, [FromServices] CreateFolderHandler handler, CancellationToken ct)
	{
		var created = await handler.HandleAsync(organizationId, body, ct);
		return Created($"api/organizations/{organizationId}/dataset-folders/{created.Id}", created);
	}

	/// <summary>Renames the folder and/or moves it under another one (parentId null is the top level).</summary>
	[HttpPut("{folderId:guid}")]
	public async Task<ActionResult<DatasetFolderDto>> Update(
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
