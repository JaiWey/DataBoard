using DataBoard.Application.Folders;
using DataBoard.Application.Abstractions;
using DataBoard.Application.Charts;
using DataBoard.Application.DataSources;
using DataBoard.Application.Charts;
using DataBoard.Application.ApiTokens;
using DataBoard.Application.Dashboards;
using DataBoard.Application.Datasets;
using DataBoard.Application.Identity;
using DataBoard.Application.Organizations;
using DataBoard.Infrastructure.DataSources;
using DataBoard.Infrastructure.Identity;
using DataBoard.Infrastructure.Persistence;
using DataBoard.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataBoard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolved when the context is first created (not at startup) so configuration added later,
        // for example by integration tests, is honoured.
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
            options.UseNpgsql(connectionString);
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IDataSourceRepository, DataSourceRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IDatasetRepository, DatasetRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IDashboardShareRepository, DashboardShareRepository>();
        services.AddScoped<IApiTokenRepository, ApiTokenRepository>();
        services.AddScoped<IChartRepository, ChartRepository>();

        services.AddDataProtection();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddSingleton<IClock, SystemClock>();

        services.Configure<ResultCacheOptions>(configuration.GetSection(ResultCacheOptions.SectionName));
        services.AddSingleton<IResultCache, MemoryResultCache>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.Key is { Length: >= JwtOptions.MinKeyBytes }, $"Jwt:Key must be at least {JwtOptions.MinKeyBytes} characters.")
            .ValidateOnStart();

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                // Length over composition rules: long passphrases are stronger than forced symbols.
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddOptions<DataSourceSecurityOptions>().Bind(configuration.GetSection(DataSourceSecurityOptions.SectionName));
        services.AddSingleton<NetworkGuard>();
        services.AddSingleton<ISqlQueryValidator, SqlParserQueryValidator>();
        services.AddSingleton<IDataSourceConnector, PostgresConnector>();
        services.AddSingleton<IChartSqlRenderer, PostgresChartSqlRenderer>();
        services.AddSingleton<ITableViewSqlRenderer, PostgresTableViewSqlRenderer>();

        services.AddSingleton<IDataSourceConnector, MsSqlDataSourceConnector>();
        services.AddSingleton<IChartSqlRenderer, MsSqlChartSqlRenderer>();
        services.AddSingleton<ITableViewSqlRenderer, MsSqlTableViewSqlRenderer>();

        services.AddSingleton<IDataSourceConnector, MySqlDataSourceConnector>();
        services.AddSingleton<IChartSqlRenderer, MySqlChartSqlRenderer>();
        services.AddSingleton<ITableViewSqlRenderer, MySqlTableViewSqlRenderer>();

        // Uploaded CSV files: imported into a small SQLite file each, then read like any other data source.
        services.Configure<UploadOptions>(configuration.GetSection(UploadOptions.SectionName));
        services.AddSingleton<IUploadStore, LocalUploadStore>();
        services.AddSingleton<IImageStore, LocalImageStore>();
        services.AddSingleton<ICsvImporter, CsvImporter>();
        services.AddSingleton<IDataSourceConnector, SqliteConnector>();
        services.AddSingleton<IChartSqlRenderer, SqliteChartSqlRenderer>();
        services.AddSingleton<ITableViewSqlRenderer, SqliteTableViewSqlRenderer>();

        services.AddScoped<JwtTokenService>();
        services.AddScoped<IIdentityService, IdentityService>();
        return services;
    }
}
