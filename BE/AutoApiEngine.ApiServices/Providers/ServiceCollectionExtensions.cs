using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using AutoApiEngine.Services.AuthServices;
using AutoApiEngine.Services.Repositories;

namespace AutoApiEngine.ApiServices.Providers
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration config)
        {
            // Register your API services here
            // e.g. services.AddScoped<IMyService, MyService>();
            services.Configure<KeyclockSettings>(config.GetSection("KeyClock"));
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
            services.AddScoped(typeof(KeycloakService));
            return services;
        }
    }
}
