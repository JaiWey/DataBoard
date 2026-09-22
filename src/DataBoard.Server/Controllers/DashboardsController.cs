using DataBoard.Application.Dashboards;
using DataBoard.Application.Folders;
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

	/// <summary>A new picture, or how the current one is shown (fit: cover, contain, tile or center; opacity 10 to 100).</summary>
	public sealed class BackgroundForm
	{
		public IFormFile? File { get; set; }
		public string? Fit { get; set; }
		public int? Opacity { get; set; }
	}

	// The picture is limited by the handler; this keeps a far bigger request from being read at all.
	private const long MaxBackgroundRequestBytes = ImageLimits.MaxBytes + 512 * 1024;

	/// <summary>
	/// Puts a picture behind the dashboard (PNG, JPEG, GIF or WebP, 5 MB at most), or, with no file, changes how the
	/// current picture is shown. Editors and Owners.
	/// </summary>
	[HttpPut("{dashboardId:guid}/background")]
	[Consumes("multipart/form-data")]
	[RequestSizeLimit(MaxBackgroundRequestBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = MaxBackgroundRequestBytes)]
	public async Task<ActionResult<DashboardDto>> SetBackground(
		Guid organizationId, Guid dashboardId, [FromForm] BackgroundForm form, [FromServices] SetDashboardBackgroundHandler handler, CancellationToken ct)
	{
		await using var stream = form.File is { Length: > 0 } ? form.File.OpenReadStream() : null;
		return Ok(await handler.HandleAsync(organizationId, dashboardId, stream, form.Fit, form.Opacity, ct));
	}

	[HttpDelete("{dashboardId:guid}/background")]
	public async Task<ActionResult<DashboardDto>> RemoveBackground(
		Guid organizationId, Guid dashboardId, [FromServices] RemoveDashboardBackgroundHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, ct));

	/// <summary>The picture itself. The address carries the picture's version (?v=), so a changed picture is never served from a cache.</summary>
	[HttpGet("{dashboardId:guid}/background")]
	public async Task<IActionResult> GetBackground(
		Guid organizationId, Guid dashboardId, [FromServices] GetDashboardBackgroundHandler handler, CancellationToken ct)
	{
		var image = await handler.HandleAsync(organizationId, dashboardId, ct);
		Response.Headers.CacheControl = "private, max-age=31536000, immutable";
		Response.Headers["X-Content-Type-Options"] = "nosniff";
		return File(image.Content, image.ContentType);
	}

	/// <summary>A picture to put on the dashboard as a widget, and how to show it.</summary>
	public sealed class ImageWidgetForm
	{
		public IFormFile? File { get; set; }
		public string? Fit { get; set; }
		public string? Alt { get; set; }
		public string? LinkUrl { get; set; }
	}

	public sealed class ImageFileForm
	{
		public IFormFile? File { get; set; }
	}

	/// <summary>
	/// Puts a picture on the dashboard as a widget (PNG, JPEG, GIF or WebP, 5 MB at most), at the bottom of the grid.
	/// Fit, description and link can be changed later with the dashboard. Editors and Owners.
	/// </summary>
	[HttpPost("{dashboardId:guid}/image-widgets")]
	[Consumes("multipart/form-data")]
	[RequestSizeLimit(MaxBackgroundRequestBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = MaxBackgroundRequestBytes)]
	public async Task<ActionResult<DashboardDto>> AddImageWidget(
		Guid organizationId, Guid dashboardId, [FromForm] ImageWidgetForm form, [FromServices] AddImageWidgetHandler handler, CancellationToken ct)
	{
		if (form.File is null || form.File.Length == 0)
			throw new Application.Common.ValidationException("File", "Choose a picture to put on the dashboard.");
		await using var stream = form.File.OpenReadStream();
		return Ok(await handler.HandleAsync(organizationId, dashboardId, stream, new ImageWidgetOptions(form.Fit, form.Alt, form.LinkUrl), ct));
	}

	/// <summary>Puts another picture in a picture widget; its place, fit, description and link stay. Editors and Owners.</summary>
	[HttpPut("{dashboardId:guid}/image-widgets/{widgetId:guid}/image")]
	[Consumes("multipart/form-data")]
	[RequestSizeLimit(MaxBackgroundRequestBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = MaxBackgroundRequestBytes)]
	public async Task<ActionResult<DashboardDto>> ReplaceImageWidget(
		Guid organizationId, Guid dashboardId, Guid widgetId, [FromForm] ImageFileForm form, [FromServices] ReplaceImageWidgetHandler handler, CancellationToken ct)
	{
		if (form.File is null || form.File.Length == 0)
			throw new Application.Common.ValidationException("File", "Choose a picture to put on the dashboard.");
		await using var stream = form.File.OpenReadStream();
		return Ok(await handler.HandleAsync(organizationId, dashboardId, widgetId, stream, ct));
	}

	/// <summary>The picture of a picture widget. The address carries its version (?v=), so a changed picture is never served from a cache.</summary>
	[HttpGet("{dashboardId:guid}/image-widgets/{widgetId:guid}/image")]
	public async Task<IActionResult> GetImageWidget(
		Guid organizationId, Guid dashboardId, Guid widgetId, [FromServices] GetImageWidgetHandler handler, CancellationToken ct)
	{
		var image = await handler.HandleAsync(organizationId, dashboardId, widgetId, ct);
		Response.Headers.CacheControl = "private, max-age=31536000, immutable";
		Response.Headers["X-Content-Type-Options"] = "nosniff";
		return File(image.Content, image.ContentType);
	}

	/// <summary>Files the dashboard in a folder (folderId null is the top level). Editors and Owners.</summary>
	[HttpPut("{dashboardId:guid}/folder")]
	public async Task<ActionResult<DashboardDto>> Move(
		Guid organizationId, Guid dashboardId, MoveToFolderRequest body, [FromServices] MoveDashboardHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, body, ct));

	[HttpDelete("{dashboardId:guid}")]
	public async Task<IActionResult> Delete(
		Guid organizationId, Guid dashboardId, [FromServices] DeleteDashboardHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, dashboardId, ct);
		return NoContent();
	}

	/// <summary>
	/// Puts the dashboard on a refresh schedule (every 5 to 1440 minutes) or, with a null interval, takes it off. The server
	/// then re-runs its charts and keeps the results for viewers. Editors and Owners.
	/// </summary>
	[HttpPut("{dashboardId:guid}/refresh")]
	public async Task<ActionResult<DashboardDto>> SetRefresh(
		Guid organizationId, Guid dashboardId, SetDashboardRefreshRequest body, [FromServices] SetDashboardRefreshHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, body, ct));

	/// <summary>Public links to the dashboard. Only Editors and Owners manage them.</summary>
	[HttpGet("{dashboardId:guid}/shares")]
	public async Task<ActionResult<IReadOnlyList<ShareDto>>> ListShares(
		Guid organizationId, Guid dashboardId, [FromServices] ListSharesHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, ct));

	/// <summary>Makes a link. The token in the answer is the only time it can be read: only its hash is kept.</summary>
	[HttpPost("{dashboardId:guid}/shares")]
	public async Task<ActionResult<ShareCreatedDto>> CreateShare(
		Guid organizationId, Guid dashboardId, CreateShareRequest? body, [FromServices] CreateShareHandler handler, CancellationToken ct)
	{
		var created = await handler.HandleAsync(organizationId, dashboardId, body, ct);
		return Created($"api/organizations/{organizationId}/dashboards/{dashboardId}/shares/{created.Id}", created);
	}

	[HttpDelete("{dashboardId:guid}/shares/{shareId:guid}")]
	public async Task<IActionResult> RevokeShare(
		Guid organizationId, Guid dashboardId, Guid shareId, [FromServices] RevokeShareHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, dashboardId, shareId, ct);
		return NoContent();
	}

	/// <summary>Runs every widget with the viewer's shared-filter values applied. Any member may run a dashboard.</summary>
	[HttpPost("{dashboardId:guid}/run")]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<DashboardRunResult>> Run(
		Guid organizationId, Guid dashboardId, RunDashboardRequest? body, [FromServices] RunDashboardHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dashboardId, body, ct));
}
