using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using AutoApiEngine.Services.DatabaseManagementServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AutoApiEngine.Presentation.Controllers
{
    [ApiController]
    [Route("api/workspaces")]
    [Authorize]
    public class WorkspacesController : BaseController
    {
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IOrganizationRepository _organizationRepository;
        private readonly IServiceProvider _serviceProvider;

        public WorkspacesController(
            IWorkspaceRepository workspaceRepository,
            IOrganizationRepository organizationRepository,
            IServiceProvider serviceProvider)
        {
            _workspaceRepository = workspaceRepository;
            _organizationRepository = organizationRepository;
            _serviceProvider = serviceProvider;
        }

        private IDatabaseManagementService GetDatabaseService(DatabaseEngine engine)
        {
            return engine switch
            {
                DatabaseEngine.MySql => _serviceProvider.GetRequiredService<MySqlDatabaseManagementService>(),
                _ => _serviceProvider.GetRequiredService<IDatabaseManagementService>()
            };
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                var workspaces = await _workspaceRepository.GetAllAsync(cancellationToken);
                return workspaces;
            });
        }

        [HttpGet("organization/{organizationId}")]
        public async Task<IActionResult> GetByOrganizationId(string organizationId, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                if (!Guid.TryParse(organizationId, out var orgId))
                    throw new ArgumentException("Invalid organization id format.");

                var workspaces = await _workspaceRepository.GetByOrganizationIdAsync(orgId, cancellationToken);
                return workspaces;
            });
        }

        [HttpGet("current-organization")]
        public async Task<IActionResult> GetCurrentOrganizationWorkspaces(CancellationToken cancellationToken = default)
        {
            var organizationName = User.FindFirst("organization")?.Value;

            if (string.IsNullOrWhiteSpace(organizationName))
                return Unauthorized(new { message = "Organization claim is missing from token." });

            return await HandleRequestAsync(async () =>
            {
                var organizations = await _organizationRepository.FindAsync(o => o.Name == organizationName, cancellationToken);
                var organization = organizations.FirstOrDefault();

                if (organization is null)
                    throw new KeyNotFoundException($"Organization '{organizationName}' was not found.");

                var workspaces = await _workspaceRepository.GetByOrganizationIdAsync(organization.Id, cancellationToken);

                return workspaces.Select(workspace => new
                {
                    workspace.Id,
                    workspace.Name,
                    workspace.DatabaseEngine,
                    workspace.IsActive,
                    workspace.LastSyncAt,
                    workspace.TablesCount,
                    workspace.FunctionsCount,
                    workspace.StoredProceduresCount,
                    workspace.DatabaseSizeBytes
                });
            });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                var workspace = await _workspaceRepository.GetByIdAsync(id, cancellationToken);
                return workspace;
            });
        }

        [HttpGet("{id:guid}/stats")]
        public async Task<IActionResult> GetStats(string id, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                var workspace = await _workspaceRepository.GetByIdAsync(id, cancellationToken);

                DatabaseStatsResult? dbStats = null;
                if (!string.IsNullOrWhiteSpace(workspace.DatabaseName))
                {
                    try
                    {
                        var dbService = GetDatabaseService(workspace.DatabaseEngine);
                        var connectionString = workspace.DatabaseEngine == DatabaseEngine.MySql
                            ? "Server=localhost;Port=3307;Uid=root;Pwd=root;"
                            : "Server=LAPTOP-II43H7KF;Trusted_Connection=True;TrustServerCertificate=True;";

                        dbStats = await dbService.GetDatabaseStatsAsync(
                            workspace.DatabaseName,
                            workspace.DatabaseEngine,
                            connectionString,
                            cancellationToken);
                    }
                    catch
                    {
                    }
                }

                return new WorkspaceStatsDto
                {
                    Id = workspace.Id,
                    Name = workspace.Name,
                    DatabaseName = workspace.DatabaseName,
                    DatabaseEngine = workspace.DatabaseEngine,
                    TablesCount = dbStats is not null ? dbStats.TablesCount : workspace.TablesCount,
                    FunctionsCount = dbStats is not null ? dbStats.FunctionsCount : workspace.FunctionsCount,
                    StoredProceduresCount = dbStats is not null ? dbStats.StoredProceduresCount : workspace.StoredProceduresCount,
                    DatabaseSizeBytes = dbStats is not null ? dbStats.DatabaseSizeBytes : (workspace.DatabaseSizeBytes ?? 0),
                    LastSyncAt = workspace.LastSyncAt,
                    IsActive = workspace.IsActive
                };
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateWorkspaceDto dto, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new ArgumentException("Workspace name is required.");

                if (!Enum.TryParse<DatabaseEngine>(dto.DatabaseEngine, true, out var engine))
                    throw new ArgumentException($"Invalid database engine: {dto.DatabaseEngine}");

                var exists = await _workspaceRepository.ExistsByNameAndOrganizationAsync(dto.Name, dto.OrganizationId, null, cancellationToken);
                if (exists)
                    throw new ArgumentException("A workspace with this name already exists in your organization. Please choose a different name.");

                var isHosted = string.IsNullOrWhiteSpace(dto.DbUserName) && string.IsNullOrWhiteSpace(dto.DatabaseName);

                var workspace = new Workspace
                {
                    Id = Guid.NewGuid(),
                    Name = dto.Name,
                    EncryptionKey = dto.EncryptionKey,
                    DbUserName = dto.DbUserName,
                    DbPassword = dto.DbPassword,
                    DatabaseEngine = engine,
                    OrganizationId = dto.OrganizationId,
                    LastSyncAt = isHosted ? DateTime.UtcNow : null,
                    TablesCount = 0,
                    FunctionsCount = 0,
                    StoredProceduresCount = 0,
                    DatabaseSizeBytes = 0
                };

                if (isHosted)
                {
                    var safeName = string.Join("_", dto.Name.Split(Path.GetInvalidFileNameChars()));
                                        var dbName = "ws_" + string.Join("", safeName.Where(c => char.IsLetterOrDigit(c) || c == '_')) + "_" + workspace.Id.ToString("N")[..8];
                    var dbService = GetDatabaseService(engine);
                    var createdName = await dbService.CreateDatabaseAsync(dbName, engine, cancellationToken);
                    workspace.DatabaseName = createdName;
                }
                else
                {
                    workspace.DatabaseName = dto.DatabaseName;
                }

                await _workspaceRepository.AddAsync(workspace, cancellationToken);
                return workspace;
            });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateWorkspaceDto dto, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                var workspace = await _workspaceRepository.GetByIdAsync(id, cancellationToken);

                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new ArgumentException("Workspace name is required.");

                if (!Enum.TryParse<DatabaseEngine>(dto.DatabaseEngine, true, out var engine))
                    throw new ArgumentException($"Invalid database engine: {dto.DatabaseEngine}");

                var duplicate = await _workspaceRepository.ExistsByNameAndOrganizationAsync(dto.Name, workspace.OrganizationId, workspace.Id, cancellationToken);
                if (duplicate)
                    throw new ArgumentException("A workspace with this name already exists in your organization. Please choose a different name.");

                workspace.Name = dto.Name;
                workspace.EncryptionKey = dto.EncryptionKey;
                workspace.DbUserName = dto.DbUserName;
                workspace.DbPassword = dto.DbPassword;
                workspace.DatabaseName = dto.DatabaseName;
                workspace.DatabaseEngine = engine;
                workspace.IsActive = dto.IsActive;

                await _workspaceRepository.UpdateAsync(workspace, cancellationToken);
                return workspace;
            });
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                await _workspaceRepository.DeleteAsync(id, cancellationToken);
                return new { message = "Workspace deleted successfully." };
            });
        }
    }
}