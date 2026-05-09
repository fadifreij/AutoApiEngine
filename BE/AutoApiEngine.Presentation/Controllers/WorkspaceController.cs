using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.AspNetCore.Mvc;

namespace AutoApiEngine.Presentation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkspacesController : BaseController
    {
        private readonly IWorkspaceRepository _workspaceRepository;

        public WorkspacesController(IWorkspaceRepository workspaceRepository)
        {
            _workspaceRepository = workspaceRepository;
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

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                var workspace = await _workspaceRepository.GetByIdAsync(id, cancellationToken);
                return workspace;
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

                var workspace = new Workspace
                {
                    Name = dto.Name,
                    EncryptionKey = dto.EncryptionKey,
                    DbUserName = dto.DbUserName,
                    DbPassword = dto.DbPassword,
                    DatabaseName = dto.DatabaseName,
                    DatabaseEngine = engine,
                    OrganizationId = dto.OrganizationId
                };

                await _workspaceRepository.AddAsync(workspace, cancellationToken);
                return workspace;
            });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateWorkspaceDto dto, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                var workspace = await _workspaceRepository.GetByIdAsync(id, cancellationToken);

                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new ArgumentException("Workspace name is required.");

                if (!Enum.TryParse<DatabaseEngine>(dto.DatabaseEngine, true, out var engine))
                    throw new ArgumentException($"Invalid database engine: {dto.DatabaseEngine}");

                workspace.Name = dto.Name;
                workspace.EncryptionKey = dto.EncryptionKey ;
                workspace.DbUserName = dto.DbUserName;
                workspace.DbPassword = dto.DbPassword;
                workspace.DatabaseName = dto.DatabaseName;
                workspace.DatabaseEngine = engine;
                workspace.IsActive = dto.IsActive;

                await _workspaceRepository.UpdateAsync(workspace, cancellationToken);
                return workspace;
            });
        }

        [HttpDelete("{id}")]
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