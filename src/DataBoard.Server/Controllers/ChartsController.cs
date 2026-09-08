using DataBoard.Application.Charts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataBoard.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}/charts")]
public class ChartsController : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<ChartDto>>> List(
		Guid organizationId, [FromServices] ListChartsHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, ct));

	[HttpGet("{chartId:guid}")]
	public async Task<ActionResult<ChartDto>> Get(
		Guid organizationId, Guid chartId, [FromServices] GetChartHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, chartId, ct));

	[HttpPost]
	public async Task<ActionResult<ChartDto>> Create(
		Guid organizationId, SaveChartRequest body, [FromServices] CreateChartHandler handler, CancellationToken ct)
	{
		var created = await handler.HandleAsync(organizationId, body, ct);
		return Created($"api/organizations/{organizationId}/charts/{created.Id}", created);
	}

	[HttpPut("{chartId:guid}")]
	public async Task<ActionResult<ChartDto>> Update(
		Guid organizationId, Guid chartId, SaveChartRequest body, [FromServices] UpdateChartHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, chartId, body, ct));

	/// <summary>Removes the chart and takes it off every dashboard that showed it.</summary>
	[HttpDelete("{chartId:guid}")]
	public async Task<IActionResult> Delete(
		Guid organizationId, Guid chartId, [FromServices] DeleteChartHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, chartId, ct);
		return NoContent();
	}

	/// <summary>Runs a saved chart. Any member may run it; only Editors and Owners get the generated SQL.</summary>
	[HttpPost("{chartId:guid}/run")]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<ChartResult>> Run(
		Guid organizationId, Guid chartId, [FromServices] RunSavedChartHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, chartId, ct));
}
