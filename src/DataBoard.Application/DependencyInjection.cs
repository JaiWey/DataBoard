using DataBoard.Application.ApiTokens;
using DataBoard.Application.Charts;
using DataBoard.Application.Dashboards;
using DataBoard.Application.DataSources;
using DataBoard.Application.Datasets;
using DataBoard.Application.Folders;
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
        services.AddScoped<TableViewSqlRenderers>();

        services.AddScoped<CreateOrganizationHandler>();
        services.AddScoped<ListMyOrganizationsHandler>();
        services.AddScoped<SeedGuestSandboxHandler>();
        services.AddScoped<AddMemberHandler>();
        services.AddScoped<SharedLinkAccess>();
        services.AddScoped<DashboardVisibility>();
        services.AddScoped<GetDashboardAccessHandler>();
        services.AddScoped<SetDashboardAccessHandler>();
        services.AddScoped<GetFolderAccessHandler>();
        services.AddScoped<SetFolderAccessHandler>();
        services.AddScoped<ShareWithOrganizationHandler>();
        services.AddScoped<ListOrgSharesHandler>();
        services.AddScoped<UnshareWithOrganizationHandler>();
        services.AddScoped<ListSharedWithMeHandler>();
        services.AddScoped<OrgSharedView>();
        services.AddScoped<GetOrgSharedDashboardHandler>();
        services.AddScoped<RunOrgSharedDashboardHandler>();
        services.AddScoped<GetOrgSharedBackgroundHandler>();
        services.AddScoped<GetOrgSharedImageWidgetHandler>();
        services.AddScoped<ListMembersHandler>();
        services.AddScoped<ChangeMemberRoleHandler>();
        services.AddScoped<RemoveMemberHandler>();

        services.AddScoped<CreateDataSourceHandler>();
        services.AddScoped<CreateCsvDataSourceHandler>();
        services.AddScoped<ReplaceCsvDataHandler>();
        services.AddScoped<DeleteDataSourceHandler>();
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
        services.AddScoped<SetDatasetCacheHandler>();
        services.AddScoped<ListFoldersHandler>();
        services.AddScoped<CreateFolderHandler>();
        services.AddScoped<UpdateFolderHandler>();
        services.AddScoped<DeleteFolderHandler>();
        services.AddScoped<MoveDatasetHandler>();
        services.AddScoped<MoveChartHandler>();
        services.AddScoped<MoveDashboardHandler>();
        services.AddScoped<PreviewDatasetHandler>();
        services.AddScoped<PreviewTableViewHandler>();
        services.AddScoped<RunChartQueryHandler>();

        services.AddScoped<CreateChartHandler>();
        services.AddScoped<ListChartsHandler>();
        services.AddScoped<GetChartHandler>();
        services.AddScoped<UpdateChartHandler>();
        services.AddScoped<DeleteChartHandler>();
        services.AddScoped<RunSavedChartHandler>();

        services.AddScoped<CreateDashboardHandler>();
        services.AddScoped<ListDashboardsHandler>();
        services.AddScoped<GetDashboardHandler>();
        services.AddScoped<UpdateDashboardHandler>();
        services.AddScoped<DeleteDashboardHandler>();
        services.AddScoped<RunDashboardHandler>();
        services.AddScoped<SetDashboardRefreshHandler>();
        services.AddScoped<RefreshDueDashboardsHandler>();
        services.AddScoped<SetDashboardBackgroundHandler>();
        services.AddScoped<RemoveDashboardBackgroundHandler>();
        services.AddScoped<GetDashboardBackgroundHandler>();
        services.AddScoped<GetSharedDashboardBackgroundHandler>();
        services.AddScoped<AddImageWidgetHandler>();
        services.AddScoped<DuplicateDashboardHandler>();
        services.AddScoped<CopyDashboardToOrganizationHandler>();
        services.AddScoped<ReplaceImageWidgetHandler>();
        services.AddScoped<GetImageWidgetHandler>();
        services.AddScoped<GetSharedImageWidgetHandler>();

        services.AddScoped<CreateApiTokenHandler>();
        services.AddScoped<ListApiTokensHandler>();
        services.AddScoped<RevokeApiTokenHandler>();
        services.AddScoped<AuthenticateApiTokenHandler>();

        services.AddScoped<CreateShareHandler>();
        services.AddScoped<ListSharesHandler>();
        services.AddScoped<RevokeShareHandler>();
        services.AddScoped<GetSharedDashboardHandler>();
        services.AddScoped<RunSharedDashboardHandler>();
        return services;
    }
}
