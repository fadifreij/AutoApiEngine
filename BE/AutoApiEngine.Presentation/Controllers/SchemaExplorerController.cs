using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using AutoApiEngine.Services.DatabaseManagementServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoApiEngine.Presentation.Controllers
{
    [ApiController]
    [Route("api/schema")]
    [Authorize]
    public class SchemaExplorerController : BaseController
    {
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly ISchemaExplorerService _schemaExplorerService;
        private readonly IServiceProvider _serviceProvider;

        public SchemaExplorerController(
            IWorkspaceRepository workspaceRepository,
            ISchemaExplorerService schemaExplorerService,
            IServiceProvider serviceProvider)
        {
            _workspaceRepository = workspaceRepository;
            _schemaExplorerService = schemaExplorerService;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Explores the schema of a workspace's database — returns all objects
        /// (tables with column counts, views, stored procedures, functions),
        /// summary counts, and database size.
        /// Supports optional search query parameter to filter by object name.
        /// </summary>
        [HttpGet("{workspaceId:guid}")]
        public async Task<IActionResult> Explore(string workspaceId, [FromQuery] string? search = null, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken);

                if (string.IsNullOrWhiteSpace(workspace.DatabaseName))
                    throw new ArgumentException("Workspace has no database associated with it.");

                var connectionString = workspace.DatabaseEngine switch
                {
                    DatabaseEngine.MySql => "Server=localhost;Port=3307;Uid=root;Pwd=root;",
                    _ => "Server=LAPTOP-II43H7KF;Trusted_Connection=True;TrustServerCertificate=True;"
                };

                var result = await _schemaExplorerService.ExploreAsync(
                    workspace.DatabaseName,
                    workspace.DatabaseEngine,
                    connectionString,
                    search,
                    cancellationToken);

                return result;
            });
        }

        /// <summary>
        /// Returns summary statistics only (counts of tables, views, functions, SPs, and database size).
        /// </summary>
        [HttpGet("{workspaceId:guid}/stats")]
        public async Task<IActionResult> GetStats(string workspaceId, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken);

                if (string.IsNullOrWhiteSpace(workspace.DatabaseName))
                    throw new ArgumentException("Workspace has no database associated with it.");

                var connectionString = workspace.DatabaseEngine switch
                {
                    DatabaseEngine.MySql => "Server=localhost;Port=3307;Uid=root;Pwd=root;",
                    _ => "Server=LAPTOP-II43H7KF;Trusted_Connection=True;TrustServerCertificate=True;"
                };

                var result = await _schemaExplorerService.ExploreAsync(
                    workspace.DatabaseName,
                    workspace.DatabaseEngine,
                    connectionString,
                    searchFilter: null,
                    cancellationToken);

                return new
                {
                    result.TablesCount,
                    result.ViewsCount,
                    result.FunctionsCount,
                    result.StoredProceduresCount,
                    result.DatabaseSizeBytes
                };
            });
        }
    }
}
