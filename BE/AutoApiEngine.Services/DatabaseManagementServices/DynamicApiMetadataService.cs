using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Implementation of <see cref="IDynamicApiMetadataService"/> that combines
    /// column metadata from the schema explorer with FK info from the FK service.
    /// </summary>
    public class DynamicApiMetadataService : IDynamicApiMetadataService
    {
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IForeignKeyService _foreignKeyService;
        private readonly DynamicApiService _dynamicApiService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DynamicApiMetadataService> _logger;

        public DynamicApiMetadataService(
            IWorkspaceRepository workspaceRepository,
            IForeignKeyService foreignKeyService,
            DynamicApiService dynamicApiService,
            IConfiguration configuration,
            ILogger<DynamicApiMetadataService> logger)
        {
            _workspaceRepository = workspaceRepository;
            _foreignKeyService = foreignKeyService;
            _dynamicApiService = dynamicApiService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<DynamicApiObjectMetadataDto> GetObjectMetadataAsync(
            string workspaceId,
            string objectName,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            var meta = await _dynamicApiService.GetTableMetadataAsync(workspace, objectName, cancellationToken);

            // Resolve object type
            var connStr = _dynamicApiService.BuildConnectionString(workspace);
            await using var connection = DynamicApiService.CreateConnection(workspace.DatabaseEngine, connStr);
            await connection.OpenAsync(cancellationToken);
            var (_, objectType) = await DynamicApiService.ResolveObjectStatic(connection, workspace.DatabaseEngine, objectName, cancellationToken);

            // Get FK info
            ForeignKeyInfoDto? fkInfo = null;
            try
            {
                fkInfo = await _foreignKeyService.GetForeignKeysAsync(
                    workspace.DatabaseName ?? "",
                    workspace.DatabaseEngine,
                    connStr,
                    objectName,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get FK info for '{Object}'", objectName);
            }

            return new DynamicApiObjectMetadataDto
            {
                ObjectName = objectName,
                ObjectType = objectType,
                Schema = meta.Schema,
                Columns = meta.Columns.Values.Select(c => new ColumnMetadataDto
                {
                    Name = c.Name,
                    DataType = c.DataType,
                    IsNullable = c.IsNullable,
                    IsPrimaryKey = c.IsPrimaryKey
                }).ToList(),
                PrimaryKeyColumns = meta.PrimaryKeyColumns,
                ForeignKeys = fkInfo?.ForeignKeys ?? new List<ForeignKeyDetailDto>(),
                ReferencedBy = fkInfo?.ReferencedBy ?? new List<ReferencedByDetailDto>()
            };
        }

        private async Task<Workspace> LoadWorkspaceAsync(string workspaceId, CancellationToken ct)
        {
            var ws = await _workspaceRepository.GetByIdAsync(workspaceId, ct);
            if (string.IsNullOrWhiteSpace(ws.DatabaseName))
                throw new ArgumentException("Workspace has no database associated with it.");
            return ws;
        }
    }
}
