using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.Common;
using AutoApiEngine.ServiceAbstraction.DTO;
using AutoApiEngine.Services.AuthServices;
using AutoApiEngine.Services.DatabaseManagementServices;
using AutoApiEngine.Services.Services;
using AutoApiEngine.Services.Repositories;
using AutoApiEngine.Services.Repositories.Common;

namespace AutoApiEngine.ApiServices.Providers
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration config)
        {
            services.Configure<KeyclockSettings>(config.GetSection("KeyClock"));
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
            services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
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

            // Schema explorer services (SQL Server + MySQL + resolver)
            services.AddScoped<SqlSchemaExplorer>();
            services.AddScoped<MySqlSchemaExplorer>();
            services.AddScoped<ISchemaExplorerService, SchemaExplorerResolver>();

            // Query execution service
            services.AddScoped<IQueryExecutionService, QueryExecutionService>();

            // DDL file management service
            services.AddScoped<IDdlFileService, DdlFileService>();

            // ── MCP-style Database Tool Service (used by AI assistant) ──
            services.AddScoped<IDatabaseToolService, DatabaseToolService>();

            // ── AI Assistant (local Ollama or any OpenAI-compatible API) ──
            //
            // AiAssistantService is registered directly as IAiAssistantService.
            // It loads system instructions from Prompts/db-copilot-instructions.md
            // and uses the configured AI provider for completions with tool calling.

            services.Configure<AiSettings>(config.GetSection("Ai"));
            services.Configure<OpenRouterAiSettings>(config.GetSection("OpenRouterAi"));

            // AI provider with IPv4 socket optimization
            services.AddHttpClient<AiAssistantService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(300);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Force IPv4 + a short connect timeout. Some networks return IPv6/NAT64
                // (64:ff9b::) addresses that silently hang on connect; this avoids the
                // long stall and connects directly over IPv4 like curl/PowerShell do.
                ConnectTimeout = TimeSpan.FromSeconds(10),
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var socket = new System.Net.Sockets.Socket(
                        System.Net.Sockets.AddressFamily.InterNetwork,
                        System.Net.Sockets.SocketType.Stream,
                        System.Net.Sockets.ProtocolType.Tcp)
                    { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                        return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            });

            // Register AiAssistantService directly as the IAiAssistantService implementation
            services.AddScoped<IAiAssistantService>(sp => sp.GetRequiredService<AiAssistantService>());

            // ── Dynamic Generic API (FK discovery + SQL builder) ──
            services.AddScoped<SqlForeignKeyService>();
            services.AddScoped<MySqlForeignKeyService>();
            services.AddScoped<IForeignKeyService, ForeignKeyServiceResolver>();
            services.AddScoped<SqlServerDynamicApiService>();
            services.AddScoped<MySqlDynamicApiService>();
            services.AddScoped<DynamicApiServiceResolver>();
            services.AddScoped<IDynamicApiService, DynamicApiServiceResolver>();
            services.AddScoped<SpVerbClassifier>();
            services.AddScoped<IDynamicApiMetadataService, DynamicApiMetadataService>();

            // ── Deployed API service ──
            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
           

            return services;
        }
    }
}
