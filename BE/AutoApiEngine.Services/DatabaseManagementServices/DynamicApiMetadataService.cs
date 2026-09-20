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
        private readonly DynamicApiServiceResolver _dynamicApiServiceResolver;
        private readonly ISchemaExplorerService _schemaExplorer;
        private readonly SpVerbClassifier _spVerbClassifier;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DynamicApiMetadataService> _logger;

        public DynamicApiMetadataService(
            IWorkspaceRepository workspaceRepository,
            IForeignKeyService foreignKeyService,
            DynamicApiServiceResolver dynamicApiServiceResolver,
            ISchemaExplorerService schemaExplorer,
            SpVerbClassifier spVerbClassifier,
            IConfiguration configuration,
            ILogger<DynamicApiMetadataService> logger)
        {
            _workspaceRepository = workspaceRepository;
            _foreignKeyService = foreignKeyService;
            _dynamicApiServiceResolver = dynamicApiServiceResolver;
            _schemaExplorer = schemaExplorer;
            _spVerbClassifier = spVerbClassifier;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<DynamicApiObjectMetadataDto> GetObjectMetadataAsync(
            string workspaceId,
            string objectName,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            var meta = await _dynamicApiServiceResolver.GetTableMetadataAsync(workspace, objectName, cancellationToken);

            // Resolve object type
            var connStr = _dynamicApiServiceResolver.BuildConnectionString(workspace);
            await using var connection = DynamicApiServiceResolver.CreateConnection(workspace.DatabaseEngine, connStr);
            await connection.OpenAsync(cancellationToken);
            var (_, objectType) = await _dynamicApiServiceResolver.ResolveObjectStatic(connection, workspace.DatabaseEngine, objectName, cancellationToken);

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

            // Resolve the HTTP verb + routine parameters per object type (plan §3.4).
            var databaseName = workspace.DatabaseName ?? "";
            string? verb;
            List<RoutineParameterDto> parameters;

            if (IsObjectType(objectType, "TABLE"))
            {
                // Table — full CRUD, no verb, no routine parameters.
                verb = null;
                parameters = new List<RoutineParameterDto>();
            }
            else if (IsObjectType(objectType, "VIEW"))
            {
                // View — read-only GET. Views have no sys.parameters /
                // information_schema.PARAMETERS rows, so the parameter list comes
                // back empty; the frontend reads view params from the query string
                // by convention (Step 10).
                verb = "GET";
                parameters = await _schemaExplorer.GetRoutineParametersAsync(
                    databaseName, workspace.DatabaseEngine, connStr, meta.Schema, objectName, cancellationToken);
            }
            else if (IsObjectType(objectType, "FUNCTION"))
            {
                // Function — always GET (run), input params resolve from the catalog.
                verb = "GET";
                parameters = await _schemaExplorer.GetRoutineParametersAsync(
                    databaseName, workspace.DatabaseEngine, connStr, meta.Schema, objectName, cancellationToken);
            }
            else
            {
                // StoredProcedure — exactly one verb classified from the definition.
                parameters = await _schemaExplorer.GetRoutineParametersAsync(
                    databaseName, workspace.DatabaseEngine, connStr, meta.Schema, objectName, cancellationToken);
                var definition = await _schemaExplorer.GetRoutineDefinitionAsync(
                    databaseName, workspace.DatabaseEngine, connStr, meta.Schema, objectName, cancellationToken);
                verb = _spVerbClassifier.Evaluate(definition) == SpVerb.Get ? "GET" : "POST";
            }

            return new DynamicApiObjectMetadataDto
            {
                ObjectName = objectName,
                ObjectType = objectType,
                Schema = meta.Schema,
                Verb = verb,
                Parameters = parameters,
                Columns = meta.Columns.Values.Select(c => new ColumnMetadataDto
                {
                    Name = c.Name,
                    DataType = c.DataType,
                    IsNullable = c.IsNullable,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsIdentity = c.IsIdentity
                }).ToList(),
                PrimaryKeyColumns = meta.PrimaryKeyColumns,
                ForeignKeys = fkInfo?.ForeignKeys ?? new List<ForeignKeyDetailDto>(),
                ReferencedBy = fkInfo?.ReferencedBy ?? new List<ReferencedByDetailDto>()
            };
        }

        /// <summary>
        /// Case-insensitive object-type check. The resolver returns uppercase types
        /// ("TABLE"/"BASE TABLE", "VIEW", "FUNCTION", "PROCEDURE") for both engines.
        /// </summary>
        private static bool IsObjectType(string objectType, string expected)
            => objectType.Contains(expected, StringComparison.OrdinalIgnoreCase);

        private async Task<Workspace> LoadWorkspaceAsync(string workspaceId, CancellationToken ct)
        {
            var ws = await _workspaceRepository.GetByIdAsync(workspaceId, ct);
            if (string.IsNullOrWhiteSpace(ws.DatabaseName))
                throw new ArgumentException("Workspace has no database associated with it.");
            return ws;
        }
    }
}
