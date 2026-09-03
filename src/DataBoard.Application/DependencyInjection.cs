using DataBoard.Application.Charts;
using DataBoard.Application.Dashboards;
using DataBoard.Application.DataSources;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using Microsoft.Extensions.DependencyInjection;

namespace DataBoard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<OrganizationAccess>();
        services.AddScoped<DataSourceConnectors>();
        services.AddScoped<ChartSqlRenderers>();

        services.AddScoped<CreateOrganizationHandler>();
        services.AddScoped<ListMyOrganizationsHandler>();
        services.AddScoped<AddMemberHandler>();

        services.AddScoped<CreateDataSourceHandler>();
        services.AddScoped<ListDataSourcesHandler>();
        services.AddScoped<TestNewConnectionHandler>();
        services.AddScoped<TestSavedConnectionHandler>();
        services.AddScoped<GetSchemaHandler>();
        services.AddScoped<PreviewQueryHandler>();

        services.AddScoped<CreateDatasetHandler>();
        services.AddScoped<ListDatasetsHandler>();
        services.AddScoped<GetDatasetHandler>();
        services.AddScoped<UpdateDatasetHandler>();
        services.AddScoped<DeleteDatasetHandler>();
        services.AddScoped<PreviewDatasetHandler>();
        services.AddScoped<RunChartQueryHandler>();

        services.AddScoped<CreateDashboardHandler>();
        services.AddScoped<ListDashboardsHandler>();
        services.AddScoped<GetDashboardHandler>();
        services.AddScoped<UpdateDashboardHandler>();
        services.AddScoped<DeleteDashboardHandler>();
        services.AddScoped<RunDashboardHandler>();
        return services;
    }
}
