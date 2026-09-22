using DataBoard.Application.Dashboards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataBoard.Server.Controllers;

/// <summary>
/// A dashboard seen through a public link. Nobody is signed in: the token in the address is the whole credential, and
/// it only ever opens that one dashboard, read-only. A token that is wrong, expired or revoked is always the same 404.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("shared")]
[Route("api/shared/dashboards/{token}")]
public class SharedDashboardsController : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<SharedDashboardDto>> Get(string token, [FromServices] GetSharedDashboardHandler handler, CancellationToken ct)
	{
		NoStore();
		return Ok(await handler.HandleAsync(token, ct));
	}

	[HttpPost("run")]
	public async Task<ActionResult<DashboardRunResult>> Run(
		string token, RunDashboardRequest? body, [FromServices] RunSharedDashboardHandler handler, CancellationToken ct)
	{
		NoStore();
		return Ok(await handler.HandleAsync(token, body, ct));
	}

	/// <summary>The picture behind the dashboard, if it has one.</summary>
	[HttpGet("background")]
	public async Task<IActionResult> Background(string token, [FromServices] GetSharedDashboardBackgroundHandler handler, CancellationToken ct)
	{
		var image = await handler.HandleAsync(token, ct);
		Response.Headers.CacheControl = "private, max-age=300";
		Response.Headers["X-Content-Type-Options"] = "nosniff";
		return File(image.Content, image.ContentType);
	}

	/// <summary>The picture of a picture widget on the dashboard.</summary>
	[HttpGet("images/{widgetId:guid}")]
	public async Task<IActionResult> Image(string token, Guid widgetId, [FromServices] GetSharedImageWidgetHandler handler, CancellationToken ct)
	{
		var image = await handler.HandleAsync(token, widgetId, ct);
		Response.Headers.CacheControl = "private, max-age=300";
		Response.Headers["X-Content-Type-Options"] = "nosniff";
		return File(image.Content, image.ContentType);
	}

	/// <summary>The data is private to whoever holds the link, so no shared cache may keep it.</summary>
	private void NoStore() => Response.Headers.CacheControl = "no-store";
}
