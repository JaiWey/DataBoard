using DataBoard.Application.ApiTokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataBoard.Server.Controllers;

/// <summary>
/// The signed-in person's own personal access tokens. These accept a sign-in (the JWT) only: a token cannot be used to
/// make or list tokens, so a leaked token cannot be used to create more of them.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me/tokens")]
public class ApiTokensController : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<ApiTokenDto>>> List([FromServices] ListApiTokensHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(ct));

	/// <summary>The secret in the answer is the only time it can be read: only its hash is kept.</summary>
	[HttpPost]
	public async Task<ActionResult<ApiTokenCreatedDto>> Create(
		CreateApiTokenRequest body, [FromServices] CreateApiTokenHandler handler, CancellationToken ct)
	{
		var created = await handler.HandleAsync(body, ct);
		return Created($"api/me/tokens/{created.Id}", created);
	}

	[HttpDelete("{tokenId:guid}")]
	public async Task<IActionResult> Revoke(Guid tokenId, [FromServices] RevokeApiTokenHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(tokenId, ct);
		return NoContent();
	}
}
