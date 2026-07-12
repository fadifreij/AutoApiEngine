using System.Text.Json;
using AutoApiEngine.Domain.Entities;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.Common;
using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.Services.Services
{
    /// <summary>
    /// Service for deploying (saving) dynamic API configurations and testing them.
    /// </summary>
    public class DeployedApiService : IDeployedApiService
    {
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IGenericRepository<DeployedApi> _deployedApiRepo;
        private readonly IDynamicApiService _dynamicApiService;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public DeployedApiService(
            IWorkspaceRepository workspaceRepository,
            IGenericRepository<DeployedApi> deployedApiRepo,
            IDynamicApiService dynamicApiService)
        {
            _workspaceRepository = workspaceRepository;
            _deployedApiRepo = deployedApiRepo;
            _dynamicApiService = dynamicApiService;
        }

        public async Task<DeployedApiDto> DeployAsync(DeployApiRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.WorkspaceId))
                throw new ArgumentException("WorkspaceId is required.");
            if (string.IsNullOrWhiteSpace(request.ObjectName))
                throw new ArgumentException("ObjectName is required.");

            // Verify workspace exists
            var workspace = await _workspaceRepository.GetByIdAsync(request.WorkspaceId, ct);

            var entity = new DeployedApi
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace.Id,
                Name = $"{request.ObjectName} API",
                ObjectName = request.ObjectName,
                SelectColumns = request.SelectColumns,
                Filters = request.Filters,
                Sorts = request.Sorts,
                PageSize = request.PageSize > 0 ? request.PageSize : 100
            };

            await _deployedApiRepo.AddAsync(entity, ct);

            return MapToDto(entity);
        }

        public async Task<List<DeployedApiDto>> ListByWorkspaceAsync(string workspaceId, CancellationToken ct = default)
        {
            if (!Guid.TryParse(workspaceId, out var wsGuid))
                throw new ArgumentException("Invalid workspace ID.");

            var all = await _deployedApiRepo.FindAsync(d => d.WorkspaceId == wsGuid && d.IsActive, ct);
            return all.Select(MapToDto).ToList();
        }

        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            // Soft-delete: mark inactive
            var entity = await _deployedApiRepo.GetByIdAsync(id, ct);
            entity.IsActive = false;
            await _deployedApiRepo.UpdateAsync(entity, ct);
        }

        public async Task<TestDeployedApiResponse> TestAsync(string deployedApiId, CancellationToken ct = default)
        {
            var entity = await _deployedApiRepo.GetByIdAsync(deployedApiId, ct);
            if (!entity.IsActive)
                throw new ArgumentException("This deployed API is no longer active.");

            var query = new DynamicApiQueryRequest
            {
                PageSize = entity.PageSize
            };

            // Parse select columns
            if (!string.IsNullOrWhiteSpace(entity.SelectColumns))
            {
                query.Select = entity.SelectColumns
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            // Parse filters
            if (!string.IsNullOrWhiteSpace(entity.Filters))
            {
                try
                {
                    var filters = JsonSerializer.Deserialize<List<FilterConfigDto>>(entity.Filters, JsonOpts);
                    if (filters?.Count > 0)
                    {
                        query.Filter = filters
                            .Where(f => !string.IsNullOrWhiteSpace(f.Column) && !string.IsNullOrWhiteSpace(f.Operator))
                            .Select(f =>
                            {
                                var prefix = f.Logic == "or" ? "or:" : "";
                                var op = f.Operator;
                                var val = f.Value ?? "";
                                return $"{prefix}{f.Column}:{op}:{val}";
                            })
                            .ToList();
                    }
                }
                catch { /* ignore parse failures */ }
            }

            // Parse sorts
            if (!string.IsNullOrWhiteSpace(entity.Sorts))
            {
                try
                {
                    var sorts = JsonSerializer.Deserialize<List<SortConfigDto>>(entity.Sorts, JsonOpts);
                    if (sorts?.Count > 0)
                    {
                        query.Sort = sorts
                            .Where(s => !string.IsNullOrWhiteSpace(s.Column))
                            .Select(s => $"{s.Column}:{s.Direction}")
                            .ToList();
                    }
                }
                catch { /* ignore parse failures */ }
            }

            var result = await _dynamicApiService.GetListAsync(
                entity.WorkspaceId.ToString(),
                entity.ObjectName,
                query,
                ct);

            return new TestDeployedApiResponse
            {
                Data = result.Data,
                TotalCount = result.Paging?.TotalCount
            };
        }

        private static DeployedApiDto MapToDto(DeployedApi entity) => new()
        {
            Id = entity.Id.ToString(),
            WorkspaceId = entity.WorkspaceId.ToString(),
            Name = entity.Name,
            ObjectName = entity.ObjectName,
            SelectColumns = entity.SelectColumns,
            Filters = entity.Filters,
            Sorts = entity.Sorts,
            PageSize = entity.PageSize,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt
        };

        // ── Internal DTOs for deserializing stored JSON ──

        private class FilterConfigDto
        {
            public string Column { get; set; } = string.Empty;
            public string Operator { get; set; } = string.Empty;
            public string? Value { get; set; }
            public string Logic { get; set; } = "and";
        }

        private class SortConfigDto
        {
            public string Column { get; set; } = string.Empty;
            public string Direction { get; set; } = "asc";
        }
    }
}
