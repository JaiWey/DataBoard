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
	/// <summary>An uploaded CSV file and how to read it. Only the file is required for a new data source (and a name).</summary>
	public sealed class CsvUploadForm
	{
		public string? Name { get; set; }
		public IFormFile? File { get; set; }

		/// <summary>What the data is called in SQL. Left out, it is made from the file's name.</summary>
		public string? TableName { get; set; }

		/// <summary>One of comma, semicolon, tab or vertical bar. Left out, it is found by looking at the file.</summary>
		public string? Delimiter { get; set; }

		/// <summary>Whether the first row holds the column names. Left out, it does.</summary>
		public bool? HasHeader { get; set; }

		public CsvImportOptions Options => new(TableName, HasHeader ?? true, Delimiter);
	}

	// The file is limited by the importer; this keeps a far bigger request from being read at all.
	private const long MaxUploadBytes = CsvLimits.MaxBytes + 1024 * 1024;

	/// <summary>
	/// Makes a data source from an uploaded CSV file: it is read into a small database of its own (each column given the type
	/// that fits what is in it), and then works like any other data source. Editors and Owners.
	/// </summary>
	[HttpPost("csv")]
	[Consumes("multipart/form-data")]
	[RequestSizeLimit(MaxUploadBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<CsvUploadResult>> UploadCsv(
		Guid organizationId, [FromForm] CsvUploadForm form, [FromServices] CreateCsvDataSourceHandler handler, CancellationToken ct)
	{
		if (form.File is null || form.File.Length == 0)
			throw new Application.Common.ValidationException("File", "Choose a CSV file to upload.");

		await using var stream = form.File.OpenReadStream();
		var created = await handler.HandleAsync(organizationId, form.Name ?? string.Empty, form.File.FileName, stream, form.Options, ct);
		return Created($"api/organizations/{organizationId}/data-sources/{created.DataSource.Id}", created);
	}

	/// <summary>Puts a new file into a CSV data source. It keeps its table name, so what was built on it carries on.</summary>
	[HttpPut("{dataSourceId:guid}/csv")]
	[Consumes("multipart/form-data")]
	[RequestSizeLimit(MaxUploadBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
	[EnableRateLimiting("connections")]
	public async Task<ActionResult<CsvUploadResult>> ReplaceCsv(
		Guid organizationId, Guid dataSourceId, [FromForm] CsvUploadForm form, [FromServices] ReplaceCsvDataHandler handler, CancellationToken ct)
	{
		if (form.File is null || form.File.Length == 0)
			throw new Application.Common.ValidationException("File", "Choose a CSV file to upload.");

		await using var stream = form.File.OpenReadStream();
		return Ok(await handler.HandleAsync(organizationId, dataSourceId, form.File.FileName, stream, form.Options, ct));
	}

	/// <summary>Removes the data source and everything built on it (datasets and their charts). Owners only.</summary>
	[HttpDelete("{dataSourceId:guid}")]
	public async Task<IActionResult> Delete(
		Guid organizationId, Guid dataSourceId, [FromServices] DeleteDataSourceHandler handler, CancellationToken ct)
	{
		await handler.HandleAsync(organizationId, dataSourceId, ct);
		return NoContent();
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
