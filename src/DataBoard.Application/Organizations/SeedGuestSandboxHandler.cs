using System.Text;
using DataBoard.Application.Charts;
using DataBoard.Application.Dashboards;
using DataBoard.Application.DataSources;
using DataBoard.Application.Datasets;
using DataBoard.Domain.Charts;

namespace DataBoard.Application.Organizations;

/// <summary>
/// Fills a new guest's sandbox with a small shop-sales example: an uploaded CSV file, a dataset over it, three charts and a
/// dashboard that shows them. It goes through the same handlers a person would use, so the example is checked like anything
/// else, and the guest has something to look at, change and build on from the first click.
/// </summary>
public sealed class SeedGuestSandboxHandler(
    ListMyOrganizationsHandler organizations,
    CreateCsvDataSourceHandler csv,
    CreateDatasetHandler datasets,
    CreateChartHandler charts,
    CreateDashboardHandler dashboards)
{
    private const string Sales = """
        sold_on,region,product,units,revenue
        2026-01-08,Auckland,Phone,12,6000
        2026-01-15,Wellington,Phone,8,4000
        2026-01-22,Christchurch,Tablet,10,3000
        2026-02-03,Auckland,Laptop,4,6000
        2026-02-11,Wellington,Tablet,9,2700
        2026-02-19,Christchurch,Phone,7,3500
        2026-02-26,Dunedin,Phone,3,1500
        2026-03-04,Auckland,Phone,15,7500
        2026-03-12,Wellington,Laptop,5,7500
        2026-03-20,Christchurch,Laptop,3,4500
        2026-03-27,Dunedin,Tablet,6,1800
        2026-04-02,Auckland,Tablet,11,3300
        2026-04-10,Wellington,Phone,13,6500
        2026-04-17,Christchurch,Phone,9,4500
        2026-04-24,Dunedin,Laptop,2,3000
        2026-05-05,Auckland,Laptop,6,9000
        2026-05-13,Wellington,Tablet,12,3600
        2026-05-21,Christchurch,Tablet,8,2400
        2026-05-29,Dunedin,Phone,5,2500
        2026-06-06,Auckland,Phone,18,9000
        2026-06-14,Wellington,Phone,10,5000
        2026-06-22,Christchurch,Laptop,4,6000
        2026-06-30,Dunedin,Tablet,7,2100
        """;

    public async Task HandleAsync(CancellationToken ct = default)
    {
        var sandbox = (await organizations.HandleAsync(ct)).Single(o => !o.IsDefault);

        using var file = new MemoryStream(Encoding.UTF8.GetBytes(Sales.Replace("\r\n", "\n") + "\n"));
        var upload = await csv.HandleAsync(sandbox.Id, "Shop sales CSV", "sales.csv", file, new CsvImportOptions(TableName: "sales"), ct);

        var dataset = await datasets.HandleAsync(sandbox.Id, new CreateDatasetRequest(
            upload.DataSource.Id, "Shop sales", "Every sale, from the example CSV file.", "SELECT * FROM sales"), ct);

        var byRegion = await charts.HandleAsync(sandbox.Id, new SaveChartRequest(
            "Revenue by region", "Which region sells the most.", dataset.Id, ChartType.Bar,
            new ChartDefinition(new ChartQuery(
                Dimensions: [new DimensionSpec("region")],
                Measures: [new MeasureSpec(Aggregation.Sum, "revenue", "Revenue")],
                OrderBy: [new OrderSpec("Revenue", SortDirection.Desc)]))), ct);

        var byMonth = await charts.HandleAsync(sandbox.Id, new SaveChartRequest(
            "Revenue by month", "How sales moved through the year.", dataset.Id, ChartType.Line,
            new ChartDefinition(new ChartQuery(
                Dimensions: [new DimensionSpec("sold_on", TimeGrain.Month)],
                Measures: [new MeasureSpec(Aggregation.Sum, "revenue", "Revenue")]))), ct);

        var total = await charts.HandleAsync(sandbox.Id, new SaveChartRequest(
            "Total revenue", null, dataset.Id, ChartType.Number,
            new ChartDefinition(new ChartQuery(Measures: [new MeasureSpec(Aggregation.Sum, "revenue", "Revenue")]))), ct);

        await dashboards.HandleAsync(sandbox.Id, new SaveDashboardRequest(
            "Sales overview", "An example dashboard. Change it, or build your own.",
            new DashboardDefinition(
                [
                    new DashboardWidget(Guid.Empty, total.Id, new WidgetLayout(0, 0, 12, 3)),
                    new DashboardWidget(Guid.Empty, byRegion.Id, new WidgetLayout(0, 3, 6, 8)),
                    new DashboardWidget(Guid.Empty, byMonth.Id, new WidgetLayout(6, 3, 6, 8)),
                ],
                [])), ct);
    }
}
