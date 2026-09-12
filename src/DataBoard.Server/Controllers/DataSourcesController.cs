using DataBoard.Application.DataSources;
using DataBoard.Application.Datasets;
using DataBoard.Domain.DataSources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataBoard.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}/data-sources")]
public class DataSourcesController : ControllerBase
{
	public sealed record CreateDataSourceBody(string Name, DataSourceType Type, string ConnectionString);
	public sealed record TestConnectionBody(DataSourceType Type, string ConnectionString);
	public sealed record PreviewBody(string Sql, int? MaxRows, IReadOnlyList<DataBoard.Application.Datasets.DatasetParameter>? Parameters = null);

	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<DataSourceDto>>> List(
		Guid organizationId, [FromServices] ListDataSourcesHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, ct));

	[HttpPost]
	public async Task<ActionResult<DataSourceDto>> Create(
		Guid organizationId, CreateDataSourceBody body, [FromServices] CreateDataSourceHandler handler, CancellationToken ct)
	{
		var created = await handler.HandleAsync(
			new CreateDataSourceRequest(organizationId, body.Name, body.Type, body.ConnectionString), ct);
		return Created($"api/organizations/{organizationId}/data-sources/{created.Id}", created);
	}
	/// <summary>Tests connection details before they are saved.</summary>
	[HttpPost("test")]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<ConnectionTestResult>> TestNew(
		Guid organizationId, TestConnectionBody body, [FromServices] TestNewConnectionHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, new TestConnectionRequest(body.Type, body.ConnectionString), ct));

	[HttpPost("{dataSourceId:guid}/test")]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<ConnectionTestResult>> TestSaved(
		Guid organizationId, Guid dataSourceId, [FromServices] TestSavedConnectionHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dataSourceId, ct));

	[HttpGet("{dataSourceId:guid}/schema")]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<DatabaseSchema>> Schema(
		Guid organizationId, Guid dataSourceId, [FromServices] GetSchemaHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dataSourceId, ct));

	/// <summary>Runs one read-only query and returns a small sample of rows.</summary>
	[HttpPost("{dataSourceId:guid}/preview")]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<QueryPreview>> Preview(
		Guid organizationId, Guid dataSourceId, PreviewBody body, [FromServices] PreviewQueryHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dataSourceId, new PreviewQueryRequest(body.Sql, body.MaxRows, body.Parameters), ct));

	/// <summary>Runs a table view definition (main table, fields, joins) without saving it. Editors and Owners only.</summary>
	[HttpPost("{dataSourceId:guid}/table-view/preview")]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<TableViewPreviewResult>> PreviewTableView(
		Guid organizationId, Guid dataSourceId, PreviewTableViewRequest body, [FromServices] PreviewTableViewHandler handler, CancellationToken ct) =>
		Ok(await handler.HandleAsync(organizationId, dataSourceId, body, ct));
}
