using System.Text.Json;
using System.Text.Json.Serialization;
using DataBoard.Server.Infrastructure;
using Microsoft.AspNetCore.Authorization;

namespace DataBoard.Server.Mcp;

public static class McpSetup
{
	public const string Path = "/mcp";

	/// <summary>Tool arguments and results use the same JSON conventions as the REST API: camelCase, and enums as text.</summary>
	public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
	{
		// The SDK freezes the options it is given, and frozen options must say how types are described.
		TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
		Converters = { new JsonStringEnumConverter() },
	};

	private const string Instructions = """
		DataBoard is a self-service BI tool: datasets hold the data, charts are saved queries over one dataset, and dashboards are made of charts.

		To build a dashboard from a request:
		1. list_organizations, then list_datasets to see the columns each dataset offers. Charts can only use those columns.
		2. For each chart: write the query and run preview_chart to check the numbers before saving. If the query is rejected, the error says what to fix.
		3. create_chart for each one that looks right. Check list_charts first: reuse a chart that already shows what is needed.
		4. create_dashboard with the chart ids, then set_widget_layout if the default places (two charts per row) are not what is wanted, and add_dashboard_filter for filters viewers should be able to type.
		Give charts and dashboards clear names. Say what was created, and its name, when done.

		Everything runs as the person who made the access token, with their roles: creating and changing needs the Editor or Owner role and a Write token. Nothing can be deleted through this endpoint, and datasets cannot be created or changed.
		""";

	public static IServiceCollection AddDataBoardMcp(this IServiceCollection services)
	{
		services.AddMcpServer(options =>
			{
				options.ServerInfo = new() { Name = "DataBoard", Version = "1.0.0" };
				options.ServerInstructions = Instructions;
			})
			// Stateless: every call stands alone, so it simply runs as the token's owner with no session to keep or hijack.
			.WithHttpTransport(options => options.Stateless = true)
			.WithTools<DataBoardTools>(Json);
		return services;
	}

	/// <summary>The MCP endpoint. Only a personal access token opens it (a sign-in does not), and calls are limited per token.</summary>
	public static IEndpointConventionBuilder MapDataBoardMcp(this WebApplication app) =>
		app.MapMcp(Path)
			.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ApiTokenDefaults.Scheme })
			.RequireRateLimiting("mcp");
}
