using System.Data.Common;
using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Dispatches <see cref="IDynamicApiService"/> calls to the concrete per-database
    /// implementation based on <see cref="Workspace.DatabaseEngine"/>.
    /// The workspace is loaded exactly once here, then handed to the concrete service's
    /// workspace-accepting overload (no second tracked-entity read).
    /// Also exposes the internal metadata helpers <see cref="DynamicApiMetadataService"/>
    /// needs (Step 6/7).
    /// </summary>
    public class DynamicApiServiceResolver : IDynamicApiService
    {
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly SqlServerDynamicApiService _sql;
        private readonly MySqlDynamicApiService _mysql;

        public DynamicApiServiceResolver(
            IWorkspaceRepository workspaceRepository,
            SqlServerDynamicApiService sql,
            MySqlDynamicApiService mysql)
        {
            _workspaceRepository = workspaceRepository;
            _sql = sql;
            _mysql = mysql;
        }

        private DynamicApiServiceBase Resolve(Workspace workspace)
            => workspace.DatabaseEngine == DatabaseEngine.MySql ? _mysql : _sql;

        private async Task<Workspace> LoadWorkspaceAsync(string workspaceId, CancellationToken ct)
        {
            var ws = await _workspaceRepository.GetByIdAsync(workspaceId, ct);
            if (string.IsNullOrWhiteSpace(ws.DatabaseName))
                throw new ArgumentException("Workspace has no database associated with it.");
            return ws;
        }

        // ─────────────────────────────────────────────────────────────────
        //  IDynamicApiService
        // ─────────────────────────────────────────────────────────────────

        public async Task<DynamicApiListResponse> GetListAsync(
            string workspaceId,
            string objectName,
            DynamicApiQueryRequest query,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await Resolve(workspace).GetListAsync(workspace, objectName, query, cancellationToken);
        }

        public async Task<DynamicApiSingleResponse> GetByIdAsync(
            string workspaceId,
            string objectName,
            string id,
            DynamicApiQueryRequest? query = null,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await Resolve(workspace).GetByIdAsync(workspace, objectName, id, query, cancellationToken);
        }

        public async Task<DynamicApiSingleResponse> GetByCompositeKeyAsync(
            string workspaceId,
            string objectName,
            Dictionary<string, string> pkValues,
            DynamicApiQueryRequest? query = null,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await Resolve(workspace).GetByCompositeKeyAsync(workspace, objectName, pkValues, query, cancellationToken);
        }

        public async Task<DynamicApiActionResponse> CreateAsync(
            string workspaceId,
            string objectName,
            Dictionary<string, object?> data,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await Resolve(workspace).CreateAsync(workspace, objectName, data, cancellationToken);
        }

        public async Task<DynamicApiActionResponse> UpdateAsync(
            string workspaceId,
            string objectName,
            string id,
            Dictionary<string, object?> data,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await Resolve(workspace).UpdateAsync(workspace, objectName, id, data, cancellationToken);
        }

        public async Task<DynamicApiActionResponse> DeleteAsync(
            string workspaceId,
            string objectName,
            string id,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await Resolve(workspace).DeleteAsync(workspace, objectName, id, cancellationToken);
        }

        public async Task<DynamicApiExecutionResponse> ExecuteRoutineAsync(
            string workspaceId,
            string objectName,
            Dictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await Resolve(workspace).ExecuteRoutineAsync(workspace, objectName, parameters, cancellationToken);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Internal metadata helpers (used by DynamicApiMetadataService in steps 6/7)
        // ─────────────────────────────────────────────────────────────────

        internal Task<DynamicApiServiceBase.TableMetadata> GetTableMetadataAsync(Workspace workspace, string objectName, CancellationToken ct)
            => Resolve(workspace).GetTableMetadataAsync(workspace, objectName, ct);

        internal string BuildConnectionString(Workspace workspace)
            => Resolve(workspace).BuildConnectionString(workspace);

        internal static DbConnection CreateConnection(DatabaseEngine engine, string connStr)
            => DynamicApiServiceBase.CreateConnection(engine, connStr);

        internal Task<(string Schema, string Type)> ResolveObjectStatic(
            DbConnection connection, DatabaseEngine engine, string objectName, CancellationToken ct)
            => engine == DatabaseEngine.MySql
                ? _mysql.ResolveObjectInternalAsync(connection, objectName, ct)
                : _sql.ResolveObjectInternalAsync(connection, objectName, ct);
    }
}