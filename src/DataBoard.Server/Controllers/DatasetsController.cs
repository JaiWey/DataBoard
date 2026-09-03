using DataBoard.Application.Charts;
using DataBoard.Application.DataSources;
using DataBoard.Application.Datasets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataBoard.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}/datasets")]
public class DatasetsController : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<DatasetDto>>> List(
		Guid organizationId, [FromServices] ListDatasetsHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, ct));

	[HttpGet("{datasetId:guid}")]
	public async Task<ActionResult<DatasetDto>> Get(
		Guid organizationId, Guid datasetId, [FromServices] GetDatasetHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, datasetId, ct));

	/// <summary>Validates the SQL, runs it against the data source to learn its columns, and saves it.</summary>
	[HttpPost]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<DatasetDto>> Create(
		Guid organizationId, CreateDatasetRequest body, [FromServices] CreateDatasetHandler handler, CancellationToken ct)
	{
		var created = await handler.HandleAsync(organizationId, body, ct);
		return Created($"api/organizations/{organizationId}/datasets/{created.Id}", created);
	}

	[HttpPut("{datasetId:guid}")]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<DatasetDto>> Update(
		Guid organizationId, Guid datasetId, UpdateDatasetRequest body, [FromServices] UpdateDatasetHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, datasetId, body, ct));

	[HttpDelete("{datasetId:guid}")]
	public async Task<IActionResult> Delete(
		Guid organizationId, Guid datasetId, [FromServices] DeleteDatasetHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, datasetId, ct);
		return NoContent();
	}

	/// <summary>Sample rows of a saved dataset. Any member may read data; the SQL itself stays hidden from viewers.</summary>
	[HttpGet("{datasetId:guid}/preview")]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<QueryPreview>> Preview(
		Guid organizationId, Guid datasetId, [FromQuery] int? maxRows, [FromServices] PreviewDatasetHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, datasetId, maxRows, ct));
	/// <summary>
	/// Aggregates a dataset: group by dimensions, apply measures, filter, sort, limit. The query is a typed description,
	/// never SQL, so it can be shared with every member of the organization.
	/// </summary>
	[HttpPost("{datasetId:guid}/query")]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<ChartResult>> Query(
		Guid organizationId, Guid datasetId, ChartQuery body, [FromServices] RunChartQueryHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, datasetId, body, ct));
}
