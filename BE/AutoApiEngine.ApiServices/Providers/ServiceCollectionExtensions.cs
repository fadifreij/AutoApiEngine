using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using AutoApiEngine.Services.AuthServices;
using AutoApiEngine.Services.DatabaseManagementServices;
using AutoApiEngine.Services.Services;
using AutoApiEngine.Services.Repositories;

namespace AutoApiEngine.ApiServices.Providers
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration config)
        {
            services.Configure<KeyclockSettings>(config.GetSection("KeyClock"));
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
            services.AddScoped(typeof(KeycloakService));
            services.AddScoped<IOrganizationRepository, OrganizationRepository>();

            // Register concrete implementations
            services.AddScoped<SqlDatabaseManagementService>();
            services.AddScoped<MySqlDatabaseManagementService>();
            // Register resolver as the primary IDatabaseManagementService
            services.AddScoped<IDatabaseManagementService, DatabaseManagementServiceResolver>();
            // Connection string builders (concrete + default interface)
            services.AddScoped<SqlServerConnectionStringBuilder>();
            services.AddScoped<MySqlConnectionStringBuilder>();
            services.AddScoped<IConnectionStringBuilder, SqlServerConnectionStringBuilder>();
            services.AddScoped<IZipService, ZipService>();
            services.AddScoped<IDdlExecutionService, DdlExecutionService>();

            return services;
        }
    }
}
