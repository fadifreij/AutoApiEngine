using System.Data.Common;
using System.Text;
using System.Text.Json;
using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Abstract, dialect-agnostic implementation of <see cref="IDynamicApiService"/>.
    /// Contains all shared orchestration (CRUD flow, SQL builders, query execution)
    /// and delegates every database-specific concern to abstract dialect hooks.
    ///
    /// Per-database subclasses (<see cref="SqlServerDynamicApiService"/>,
    /// <see cref="MySqlDynamicApiService"/>) implement the hooks; a
    /// <see cref="DynamicApiServiceResolver"/> dispatches calls by engine.
    /// </summary>
    public abstract class DynamicApiServiceBase : IDynamicApiService
    {
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IForeignKeyService _foreignKeyService;
        private readonly ILogger _logger;

        private const int MaxInValues = 100;
        private const int MaxPageSize = 1000;
        private const int DefaultPageSize = 100;

        // ─────────────────────────────────────────────────────────────────
        //  Nested metadata records (kept internal so DynamicApiMetadataService
        //  and the resolver can reference them; nested to avoid the CS0101
        //  collision with the legacy namespace-level records of the same name).
        // ─────────────────────────────────────────────────────────────────

        protected internal record ColumnInfo(string Name, string DataType, bool IsNullable, bool IsPrimaryKey, bool IsIdentity);

        protected internal record TableMetadata(
            string Schema,
            string TableName,
            Dictionary<string, ColumnInfo> Columns,
            List<string> PrimaryKeyColumns
        );

        protected DynamicApiServiceBase(
            IWorkspaceRepository workspaceRepository,
            IForeignKeyService foreignKeyService,
            IConfiguration configuration,
            ILogger logger)
        {
            _workspaceRepository = workspaceRepository;
            _foreignKeyService = foreignKeyService;
            Configuration = configuration;
            _logger = logger;
        }

        protected IConfiguration Configuration { get; }

        protected ILogger Logger => _logger;

        // ─────────────────────────────────────────────────────────────────
        //  Dialect hooks
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Opening identifier quote (e.g. "[" for SQL Server, "`" for MySQL).</summary>
        protected abstract string QuoteIdentifier { get; }

        /// <summary>Closing identifier quote (e.g. "]" for SQL Server, "`" for MySQL).</summary>
        protected abstract string QuoteClose { get; }

        /// <summary>ADO.NET provider factory for the dialect.</summary>
        protected abstract DbProviderFactory GetProviderFactory();

        /// <summary>
        /// SQL that lists a table's columns with (name, full-type, nullable, isPrimaryKey, isIdentity)
        /// as five columns, filtered by @table and @schema.
        /// </summary>
        protected abstract string GetColumnsSql();

        /// <summary>
        /// SQL that lists a view/object's columns with (name, full-type, nullable) as
        /// three columns, filtered by @table and @schema.
        /// </summary>
        protected abstract string GetObjectColumnsSql();

        /// <summary>Appends the dialect-specific paging clause to a complete SELECT.</summary>
        protected abstract string ApplyPaging(string selectSql, int pageSize, int offset);

        /// <summary>
        /// Resolves an object name to its (schema, type) where type is one of
        /// "TABLE", "BASE TABLE", "VIEW", "PROCEDURE", "FUNCTION".
        /// </summary>
        protected abstract Task<(string Schema, string Type)> ResolveObjectAsync(DbConnection connection, string objectName, CancellationToken ct);

        /// <summary>Reads the columns of a non-table object (view) using the dialect's object-column query.</summary>
        protected abstract Task<Dictionary<string, ColumnInfo>> GetObjectColumnsAsync(DbConnection connection, string schema, string objectName, CancellationToken ct);

        /// <summary>
        /// Executes the parmeterized INSERT and returns the inserted row:
        /// SQL Server uses OUTPUT INSERTED.*; MySQL executes then re-selects by LAST_INSERT_ID()/PK.
        /// </summary>
        /// <param name="workspace">The workspace (has the connection-string inputs).</param>
        /// <param name="meta">Resolved table metadata.</param>
        /// <param name="objectName">The target table.</param>
        /// <param name="filtered">The non-identity column/value pairs being inserted (in parameter order).</param>
        /// <param name="quotedColumns">Comma-joined, identifier-quoted column list for the INSERT (prefix).</param>
        /// <param name="paramPlaceholders">Comma-joined @pN placeholders matching <paramref name="parameters"/> order.</param>
        /// <param name="parameters">The DbParameters the base created (names @p0..@pN in filtered order).</param>
        /// <param name="ct">Cancellation token.</param>
        protected abstract Task<Dictionary<string, object?>?> InsertReturnRowAsync(
            Workspace workspace, TableMetadata meta, string objectName,
            Dictionary<string, object?> filtered, string quotedColumns, string paramPlaceholders,
            List<DbParameter> parameters, CancellationToken ct);

        /// <summary>
        /// Executes the parameterized UPDATE and returns the updated row
        /// (SQL Server: OUTPUT INSERTED.*; MySQL: execute then re-select by PK).
        /// Returns null when no row matched the PK.
        /// </summary>
        /// <param name="setClauseSql">Comma-joined "col = @pN" SET clauses (already quoted).</param>
        protected abstract Task<Dictionary<string, object?>?> UpdateReturnRowAsync(
            Workspace workspace, TableMetadata meta, string objectName, string id, string pkCol,
            string setClauseSql, Dictionary<string, object?> filtered, List<DbParameter> parameters, CancellationToken ct);

        /// <summary>
        /// Executes the parameterized DELETE and returns a non-null marker on success,
        /// null when no row matched the PK
        /// (SQL Server: OUTPUT DELETED.*; MySQL: ExecuteNonQuery + affected count).
        /// </summary>
        protected abstract Task<Dictionary<string, object?>?> DeleteReturnRowAsync(
            Workspace workspace, TableMetadata meta, string objectName, string id,
            List<DbParameter> parameters, CancellationToken ct);

        /// <summary>
        /// Builds a connection string for the workspace using the dialect's builder/rules.
        /// </summary>
        protected internal abstract string BuildConnectionString(Workspace workspace);

        /// <summary>
        /// Executes a stored procedure or function (EXEC/CALL or SELECT fn) returning
        /// all result sets, captured OUT/INOUT parameter values, and rows affected.
        /// </summary>
        protected abstract Task<DynamicApiExecutionResponse> ExecuteRoutineInternalAsync(
            Workspace workspace, string objectName, Dictionary<string, object?> parameters, CancellationToken ct);

        // ─────────────────────────────────────────────────────────────────
        //  Public API (workspaceId entry points)
        // ─────────────────────────────────────────────────────────────────

        public async Task<DynamicApiListResponse> GetListAsync(
            string workspaceId,
            string objectName,
            DynamicApiQueryRequest query,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await GetListAsync(workspace, objectName, query, cancellationToken);
        }

        public async Task<DynamicApiSingleResponse> GetByIdAsync(
            string workspaceId,
            string objectName,
            string id,
            DynamicApiQueryRequest? query = null,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await GetByIdAsync(workspace, objectName, id, query, cancellationToken);
        }

        public async Task<DynamicApiSingleResponse> GetByCompositeKeyAsync(
            string workspaceId,
            string objectName,
            Dictionary<string, string> pkValues,
            DynamicApiQueryRequest? query = null,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await GetByCompositeKeyAsync(workspace, objectName, pkValues, query, cancellationToken);
        }

        public async Task<DynamicApiActionResponse> CreateAsync(
            string workspaceId,
            string objectName,
            Dictionary<string, object?> data,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await CreateAsync(workspace, objectName, data, cancellationToken);
        }

        public async Task<DynamicApiActionResponse> UpdateAsync(
            string workspaceId,
            string objectName,
            string id,
            Dictionary<string, object?> data,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await UpdateAsync(workspace, objectName, id, data, cancellationToken);
        }

        public async Task<DynamicApiActionResponse> DeleteAsync(
            string workspaceId,
            string objectName,
            string id,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await DeleteAsync(workspace, objectName, id, cancellationToken);
        }

        public async Task<DynamicApiExecutionResponse> ExecuteRoutineAsync(
            string workspaceId,
            string objectName,
            Dictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken);
            return await ExecuteRoutineAsync(workspace, objectName, parameters, cancellationToken);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Workspace-accepting overloads (used by the resolver to avoid
        //  loading the workspace a second time)
        // ─────────────────────────────────────────────────────────────────

        internal virtual async Task<DynamicApiListResponse> GetListAsync(Workspace workspace, string objectName, DynamicApiQueryRequest query, CancellationToken ct)
        {
            var meta = await GetTableMetadataAsync(workspace, objectName, ct);

            // Fetch FK info if query selects related columns
            var relationNames = ExtractRelationNames(query);
            ForeignKeyInfoDto? fkInfo = null;
            if (relationNames.Count > 0)
            {
                var connStr = BuildConnectionString(workspace);
                fkInfo = await _foreignKeyService.GetForeignKeysAsync(
                    workspace.DatabaseName ?? "",
                    workspace.DatabaseEngine,
                    connStr,
                    meta.TableName,
                    ct);
            }

            var (sql, parameters) = BuildSelectQuery(meta, query, includePaging: true, fkInfo);
            _logger.LogDebug("Dynamic GET list: {Sql}", sql);
            var data = await ExecuteQueryAsync(workspace, sql, parameters, ct);

            var response = new DynamicApiListResponse { Data = data };

            if (query.PageSize > 0)
            {
                var (countSql, countParams) = BuildCountQuery(meta, query);
                var connStr = BuildConnectionString(workspace);
                await using var conn = CreateConnection(connStr);
                await conn.OpenAsync(ct);
                var totalCount = await ExecuteScalarAsync<long>(conn, countSql, countParams, ct);
                response.Paging = new DynamicApiPagingInfo
                {
                    Page = Math.Max(1, query.Page),
                    PageSize = query.PageSize,
                    TotalCount = totalCount,
                    TotalPages = (int)Math.Ceiling((double)totalCount / query.PageSize)
                };
            }

            return response;
        }

        internal virtual async Task<DynamicApiSingleResponse> GetByIdAsync(
            Workspace workspace, string objectName, string id, DynamicApiQueryRequest? query, CancellationToken ct)
        {
            var meta = await GetTableMetadataAsync(workspace, objectName, ct);

            if (meta.PrimaryKeyColumns.Count != 1)
                throw new ArgumentException(
                    $"Table '{objectName}' has {meta.PrimaryKeyColumns.Count} PK columns. " +
                    "Use the composite key endpoint or query parameters instead.");

            var pkCol = meta.PrimaryKeyColumns[0];
            query ??= new DynamicApiQueryRequest();

            var safePageSize = query.PageSize;
            query.PageSize = 1;
            query.Page = 1;

            try
            {
                // Temporarily inject PK filter
                var existingFilters = query.Filter ?? new List<string>();
                query.Filter = new List<string>(existingFilters) { $"{pkCol}:eq:{id}" };

                // Fetch FK info if query selects related columns
                var relationNames = ExtractRelationNames(query);
                ForeignKeyInfoDto? fkInfo = null;
                if (relationNames.Count > 0)
                {
                    var connStr = BuildConnectionString(workspace);
                    fkInfo = await _foreignKeyService.GetForeignKeysAsync(
                        workspace.DatabaseName ?? "",
                        workspace.DatabaseEngine,
                        connStr,
                        meta.TableName,
                        ct);
                }

                var (sql, parameters) = BuildSelectQuery(meta, query, includePaging: false, fkInfo);
                _logger.LogDebug("Dynamic GET by ID: {Sql}", sql);
                var data = await ExecuteQueryAsync(workspace, sql, parameters, ct);

                var record = data.FirstOrDefault();
                if (record == null)
                    throw new KeyNotFoundException($"Record with {pkCol} = '{id}' not found in '{objectName}'.");

                return new DynamicApiSingleResponse { Data = record };
            }
            finally
            {
                query.PageSize = safePageSize;
            }
        }

        internal virtual async Task<DynamicApiSingleResponse> GetByCompositeKeyAsync(
            Workspace workspace, string objectName, Dictionary<string, string> pkValues, DynamicApiQueryRequest? query, CancellationToken ct)
        {
            var meta = await GetTableMetadataAsync(workspace, objectName, ct);

            foreach (var pkCol in meta.PrimaryKeyColumns)
            {
                if (!pkValues.ContainsKey(pkCol))
                    throw new ArgumentException($"Missing PK column '{pkCol}' in composite key.");
            }

            query ??= new DynamicApiQueryRequest();
            var existingFilters = query.Filter ?? new List<string>();
            var pkFilters = pkValues.Select(kvp => $"{kvp.Key}:eq:{kvp.Value}").ToList();
            query.Filter = new List<string>(existingFilters.Concat(pkFilters));

            var savedPageSize = query.PageSize;
            query.PageSize = 1;
            query.Page = 1;

            try
            {
                // Fetch FK info if query selects related columns
                var relationNames = ExtractRelationNames(query);
                ForeignKeyInfoDto? fkInfo = null;
                if (relationNames.Count > 0)
                {
                    var connStr = BuildConnectionString(workspace);
                    fkInfo = await _foreignKeyService.GetForeignKeysAsync(
                        workspace.DatabaseName ?? "",
                        workspace.DatabaseEngine,
                        connStr,
                        meta.TableName,
                        ct);
                }

                var (sql, parameters) = BuildSelectQuery(meta, query, includePaging: false, fkInfo);
                _logger.LogDebug("Dynamic GET by composite key: {Sql}", sql);
                var data = await ExecuteQueryAsync(workspace, sql, parameters, ct);

                var record = data.FirstOrDefault();
                if (record == null)
                    throw new KeyNotFoundException($"Record not found in '{objectName}' with the given keys.");

                return new DynamicApiSingleResponse { Data = record };
            }
            finally
            {
                query.PageSize = savedPageSize;
            }
        }

        internal virtual async Task<DynamicApiActionResponse> CreateAsync(Workspace workspace, string objectName, Dictionary<string, object?> data, CancellationToken ct)
        {
            var meta = await GetTableMetadataAsync(workspace, objectName, ct);

            // Exclude identity/auto-increment columns — they are generated by the DB
            var filtered = data
                .Where(kvp => meta.Columns.TryGetValue(kvp.Key, out var ci) && !ci.IsIdentity)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var skipped = data.Keys
                .Where(k => meta.Columns.TryGetValue(k, out var ci) && ci.IsIdentity)
                .ToList();
            if (skipped.Count > 0)
                _logger.LogDebug("Skipping identity columns from INSERT: {Columns}", string.Join(", ", skipped));

            if (filtered.Count == 0)
                throw new ArgumentException("Request body must contain at least one non-identity column-value pair.");

            ValidateColumns(meta, filtered.Keys);

            var qi = QuoteIdentifier;
            var qiClose = QuoteClose;
            var columns = string.Join(", ", filtered.Keys.Select(c => $"{qi}{c}{qiClose}"));
            var paramNames = string.Join(", ", filtered.Keys.Select((c, i) => $"@p{i}"));
            var parameters = new List<DbParameter>();
            var idx = 0;
            foreach (var kvp in filtered)
            {
                parameters.Add(CreateParam($"@p{idx}", kvp.Value, meta.Columns[kvp.Key]));
                idx++;
            }

            _logger.LogDebug("Dynamic INSERT: ({Columns}) VALUES ({ParamNames})", columns, paramNames);

            var inserted = await InsertReturnRowAsync(workspace, meta, objectName, filtered, columns, paramNames, parameters, ct);

            return new DynamicApiActionResponse
            {
                Data = inserted,
                Message = "Record created successfully."
            };
        }

        internal virtual async Task<DynamicApiActionResponse> UpdateAsync(Workspace workspace, string objectName, string id, Dictionary<string, object?> data, CancellationToken ct)
        {
            var meta = await GetTableMetadataAsync(workspace, objectName, ct);

            // Exclude identity/auto-increment columns — they cannot be updated
            var filtered = data
                .Where(kvp => meta.Columns.TryGetValue(kvp.Key, out var ci) && !ci.IsIdentity)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var skipped = data.Keys
                .Where(k => meta.Columns.TryGetValue(k, out var ci) && ci.IsIdentity)
                .ToList();
            if (skipped.Count > 0)
                _logger.LogDebug("Skipping identity columns from UPDATE: {Columns}", string.Join(", ", skipped));

            if (filtered.Count == 0)
                throw new ArgumentException("Request body must contain at least one non-identity column-value pair.");

            if (meta.PrimaryKeyColumns.Count != 1)
                throw new ArgumentException(
                    $"Table '{objectName}' has {meta.PrimaryKeyColumns.Count} PK columns. " +
                    "Use query parameters for composite key updates.");

            ValidateColumns(meta, filtered.Keys);

            var pkCol = meta.PrimaryKeyColumns[0];
            var qi = QuoteIdentifier;
            var qiClose = QuoteClose;

            var setClauses = filtered.Keys.Select((c, i) => $"{qi}{c}{qiClose} = @p{i}");
            var parameters = new List<DbParameter>();
            var idx = 0;
            foreach (var kvp in filtered)
            {
                parameters.Add(CreateParam($"@p{idx}", kvp.Value, meta.Columns[kvp.Key]));
                idx++;
            }
            parameters.Add(CreateParam("@pk", id, meta.Columns[pkCol]));

            var setClauseSql = string.Join(", ", setClauses);
            _logger.LogDebug("Dynamic UPDATE: SET {Sets} WHERE {Pk} = @pk", setClauseSql, pkCol);

            var updated = await UpdateReturnRowAsync(workspace, meta, objectName, id, pkCol, setClauseSql, filtered, parameters, ct);

            if (updated == null)
                throw new KeyNotFoundException($"Record with {pkCol} = '{id}' not found in '{objectName}'.");

            return new DynamicApiActionResponse
            {
                Data = updated,
                Message = "Record updated successfully."
            };
        }

        internal virtual async Task<DynamicApiActionResponse> DeleteAsync(Workspace workspace, string objectName, string id, CancellationToken ct)
        {
            var meta = await GetTableMetadataAsync(workspace, objectName, ct);

            if (meta.PrimaryKeyColumns.Count != 1)
                throw new ArgumentException(
                    $"Table '{objectName}' has {meta.PrimaryKeyColumns.Count} PK columns. " +
                    "Use query parameters for composite key deletes.");

            var pkCol = meta.PrimaryKeyColumns[0];
            var qi = QuoteIdentifier;
            var qiClose = QuoteClose;
            var parameters = new List<DbParameter>
            {
                CreateParam("@pk", id, meta.Columns[pkCol])
            };

            _logger.LogDebug("Dynamic DELETE: WHERE {Pk} = @pk", pkCol);

            var deleted = await DeleteReturnRowAsync(workspace, meta, objectName, id, parameters, ct);
            if (deleted == null)
                throw new KeyNotFoundException($"Record with {pkCol} = '{id}' not found in '{objectName}'.");

            return new DynamicApiActionResponse
            {
                Message = "Record deleted successfully."
            };
        }

        internal virtual Task<DynamicApiExecutionResponse> ExecuteRoutineAsync(
            Workspace workspace, string objectName, Dictionary<string, object?> parameters, CancellationToken ct)
        {
            return ExecuteRoutineInternalAsync(workspace, objectName, parameters, ct);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Table Metadata
        // ─────────────────────────────────────────────────────────────────

        internal virtual async Task<TableMetadata> GetTableMetadataAsync(Workspace workspace, string objectName, CancellationToken ct)
        {
            var connStr = BuildConnectionString(workspace);
            await using var connection = CreateConnection(connStr);
            await connection.OpenAsync(ct);

            var (schema, objectType) = await ResolveObjectAsync(connection, objectName, ct);

            if (objectType != "TABLE" && objectType != "BASE TABLE")
            {
                var viewCols = await GetObjectColumnsAsync(connection, schema, objectName, ct);
                return new TableMetadata(schema, objectName, viewCols, new List<string>());
            }

            var columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
            var pkColumns = new List<string>();
            var colSql = GetColumnsSql();

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = colSql;
                cmd.Parameters.Add(CreateParam("@schema", schema));
                cmd.Parameters.Add(CreateParam("@table", objectName));

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var col = new ColumnInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2) == "YES", reader.GetInt32(3) == 1, reader.GetInt32(4) == 1);
                    columns[col.Name] = col;
                    if (col.IsPrimaryKey) pkColumns.Add(col.Name);
                }
            }

            if (columns.Count == 0)
                throw new ArgumentException($"Object '{objectName}' not found or has no accessible columns.");

            return new TableMetadata(schema, objectName, columns, pkColumns);
        }

        // ─────────────────────────────────────────────────────────────────
        //  SQL Builders
        // ─────────────────────────────────────────────────────────────────

        private (string Sql, List<DbParameter> Parameters) BuildSelectQuery(
            TableMetadata meta, DynamicApiQueryRequest query, bool includePaging,
            ForeignKeyInfoDto? fkInfo = null)
        {
            var parameters = new List<DbParameter>();
            var qi = QuoteIdentifier;
            var qiClose = QuoteClose;
            var sb = new StringBuilder();

            // SELECT
            sb.Append("SELECT ");
            var selectStr = query.Select is { Count: > 0 } ? string.Join(",", query.Select) : null;
            var selectItems = BuildSelectList(meta, selectStr, qi, qiClose);
            sb.Append(string.Join(", ", selectItems));

            sb.Append($" FROM {qi}{meta.Schema}{qiClose}.{qi}{meta.TableName}{qiClose}");

            // LEFT JOINs for related columns
            if (fkInfo != null)
            {
                var relationNames = ExtractRelationNames(query);
                if (relationNames.Count > 0)
                {
                    var joinClauses = BuildJoinClauses(
                        meta.TableName, meta.Schema, relationNames, fkInfo, qi, qiClose);
                    sb.Append(joinClauses);
                }
            }

            // WHERE
            if (query.Filter != null && query.Filter.Count > 0)
            {
                var (whereSql, filterParams) = BuildWhereClause(meta, query.Filter);
                if (!string.IsNullOrWhiteSpace(whereSql))
                {
                    sb.Append($" WHERE {whereSql}");
                    parameters.AddRange(filterParams);
                }
            }

            // ORDER BY
            var sortStr = query.Sort is { Count: > 0 } ? string.Join(",", query.Sort) : null;
            sb.Append($" ORDER BY {BuildOrderByClause(meta, sortStr)}");

            // OFFSET/FETCH or LIMIT (dialect hook)
            var sql = sb.ToString();
            if (includePaging && query.PageSize > 0)
            {
                var offset = Math.Max(0, (query.Page - 1) * query.PageSize);
                sql = ApplyPaging(sql, query.PageSize, offset);
            }

            return (sql, parameters);
        }

        private (string Sql, List<DbParameter> Parameters) BuildCountQuery(
            TableMetadata meta, DynamicApiQueryRequest query)
        {
            var parameters = new List<DbParameter>();
            var qi = QuoteIdentifier;
            var qiClose = QuoteClose;
            var sb = new StringBuilder();

            sb.Append($"SELECT COUNT(*) FROM {qi}{meta.Schema}{qiClose}.{qi}{meta.TableName}{qiClose}");

            if (query.Filter != null && query.Filter.Count > 0)
            {
                var (whereSql, filterParams) = BuildWhereClause(meta, query.Filter);
                if (!string.IsNullOrWhiteSpace(whereSql))
                {
                    sb.Append($" WHERE {whereSql}");
                    parameters.AddRange(filterParams);
                }
            }

            return (sb.ToString(), parameters);
        }

        /// <summary>
        /// Extracts unique relation names from dotted select items (e.g. "Categories.CategoryID" → "Categories").
        /// </summary>
        private static HashSet<string> ExtractRelationNames(DynamicApiQueryRequest query)
        {
            var relations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (query.Select == null) return relations;

            foreach (var item in query.Select)
            {
                var dotIdx = item.IndexOf('.');
                if (dotIdx > 0)
                    relations.Add(item[..dotIdx]);
            }
            return relations;
        }

        /// <summary>
        /// Builds LEFT JOIN clauses for each relation referenced in the SELECT list,
        /// using foreign key metadata to resolve the join conditions.
        /// </summary>
        private static string BuildJoinClauses(
            string mainTable,
            string mainSchema,
            HashSet<string> relationNames,
            ForeignKeyInfoDto fkInfo,
            string qi,
            string qiClose)
        {
            var mainAlias = $"{qi}{mainSchema}{qiClose}.{qi}{mainTable}{qiClose}";
            var joins = new StringBuilder();

            foreach (var relation in relationNames)
            {
                // Case 1: Main table has an outgoing FK pointing to the related table.
                var outgoingFk = fkInfo.ForeignKeys.FirstOrDefault(fk =>
                    string.Equals(fk.ReferencedTable, relation, StringComparison.OrdinalIgnoreCase));

                if (outgoingFk != null)
                {
                    joins.Append(
                        $" LEFT JOIN {qi}{outgoingFk.ReferencedSchema}{qiClose}.{qi}{relation}{qiClose}" +
                        $" AS {qi}__{relation}{qiClose}" +
                        $" ON {qi}__{relation}{qiClose}.{qi}{outgoingFk.ReferencedColumn}{qiClose}" +
                        $" = {mainAlias}.{qi}{outgoingFk.Column}{qiClose}");
                    continue;
                }

                // Case 2: The related table has an incoming FK pointing to the main table.
                var incomingFk = fkInfo.ReferencedBy.FirstOrDefault(rb =>
                    string.Equals(rb.Table, relation, StringComparison.OrdinalIgnoreCase));

                if (incomingFk != null)
                {
                    joins.Append(
                        $" LEFT JOIN {qi}{incomingFk.TableSchema}{qiClose}.{qi}{relation}{qiClose}" +
                        $" AS {qi}__{relation}{qiClose}" +
                        $" ON {qi}__{relation}{qiClose}.{qi}{incomingFk.Column}{qiClose}" +
                        $" = {mainAlias}.{qi}{incomingFk.ReferencedColumn}{qiClose}");
                    continue;
                }

                throw new ArgumentException(
                    $"Cannot select related column '{relation}.*' — no foreign key relationship found " +
                    $"between '{mainTable}' and '{relation}'. Ensure the relation name matches an existing FK.");
            }

            return joins.ToString();
        }

        private static List<string> BuildSelectList(TableMetadata meta, string? select, string qi, string qiClose)
        {
            if (string.IsNullOrWhiteSpace(select))
                return meta.Columns.Keys.Select(c => $"{qi}{c}{qiClose}").ToList();

            var items = new List<string>();
            var parts = select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                var dotIdx = part.IndexOf('.');
                if (dotIdx > 0)
                {
                    var relation = part[..dotIdx];
                    var column = part[(dotIdx + 1)..];
                    items.Add($"{qi}__{relation}{qiClose}.{qi}{column}{qiClose}");
                }
                else
                {
                    if (!meta.Columns.ContainsKey(part))
                        throw new ArgumentException($"Column '{part}' does not exist on '{meta.TableName}'.");
                    items.Add($"{qi}{part}{qiClose}");
                }
            }

            return items;
        }

        private (string WhereSql, List<DbParameter> Parameters) BuildWhereClause(
            TableMetadata meta, List<string> filters)
        {
            var andClauses = new List<string>();
            var orClauses = new List<string>();
            var parameters = new List<DbParameter>();
            var pIdx = 0;
            var qi = QuoteIdentifier;
            var qiClose = QuoteClose;

            foreach (var filter in filters)
            {
                var isOr = filter.StartsWith("or:", StringComparison.OrdinalIgnoreCase);
                var expr = isOr ? filter[3..] : filter;

                var parts = expr.Split(':', 3);
                if (parts.Length < 2)
                    throw new ArgumentException($"Invalid filter: '{filter}'. Format: column:operator:value");

                var column = parts[0];
                var op = parts[1].ToLowerInvariant();
                var value = parts.Length > 2 ? parts[2] : null;

                if (!meta.Columns.ContainsKey(column))
                    throw new ArgumentException($"Filter column '{column}' does not exist on '{meta.TableName}'.");

                var colRef = $"{qi}{column}{qiClose}";
                string clause;

                switch (op)
                {
                    case "eq":
                        clause = $"{colRef} = @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column])); pIdx++;
                        break;
                    case "neq":
                        clause = $"{colRef} <> @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column])); pIdx++;
                        break;
                    case "gt":
                        clause = $"{colRef} > @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column])); pIdx++;
                        break;
                    case "gte":
                        clause = $"{colRef} >= @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column])); pIdx++;
                        break;
                    case "lt":
                        clause = $"{colRef} < @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column])); pIdx++;
                        break;
                    case "lte":
                        clause = $"{colRef} <= @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column])); pIdx++;
                        break;
                    case "contains":
                        clause = $"{colRef} LIKE @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", $"%{EscapeLike(value ?? "")}%", meta.Columns[column])); pIdx++;
                        break;
                    case "startswith":
                        clause = $"{colRef} LIKE @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", $"{EscapeLike(value ?? "")}%", meta.Columns[column])); pIdx++;
                        break;
                    case "endswith":
                        clause = $"{colRef} LIKE @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", $"%{EscapeLike(value ?? "")}", meta.Columns[column])); pIdx++;
                        break;
                    case "in":
                        var vals = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (vals.Length > MaxInValues) throw new ArgumentException($"IN exceeds max {MaxInValues} values.");
                        if (vals.Length == 0) throw new ArgumentException("IN requires at least one value.");
                        var phs = string.Join(", ", vals.Select((_, i) => $"@p{pIdx + i}"));
                        for (int i = 0; i < vals.Length; i++)
                            parameters.Add(CreateParam($"@p{pIdx + i}", vals[i], meta.Columns[column]));
                        clause = $"{colRef} IN ({phs})";
                        pIdx += vals.Length;
                        break;
                    case "isnull":
                        clause = $"{colRef} IS NULL";
                        break;
                    case "isnotnull":
                        clause = $"{colRef} IS NOT NULL";
                        break;
                    default:
                        throw new ArgumentException($"Unknown operator '{op}'. Supported: eq, neq, gt, gte, lt, lte, contains, startswith, endswith, in, isnull, isnotnull.");
                }

                if (isOr) orClauses.Add(clause);
                else andClauses.Add(clause);
            }

            var parts2 = new[] { andClauses.Count > 0 ? string.Join(" AND ", andClauses) : null,
                                 orClauses.Count > 0 ? $"({string.Join(" OR ", orClauses)})" : null }
                         .Where(p => p != null).ToList();
            return (string.Join(" AND ", parts2), parameters);
        }

        private string BuildOrderByClause(TableMetadata meta, string? sort)
        {
            var qi = QuoteIdentifier;
            var qiClose = QuoteClose;

            if (!string.IsNullOrWhiteSpace(sort))
            {
                var items = new List<string>();
                foreach (var part in sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var sub = part.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var col = sub[0];
                    var dir = sub.Length > 1 && sub[1].StartsWith("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
                    if (!meta.Columns.ContainsKey(col))
                        throw new ArgumentException($"Sort column '{col}' not found on '{meta.TableName}'.");
                    items.Add($"{qi}{col}{qiClose} {dir}");
                }
                return string.Join(", ", items);
            }

            if (meta.PrimaryKeyColumns.Count > 0)
                return string.Join(", ", meta.PrimaryKeyColumns.Select(pk => $"{qi}{pk}{qiClose} ASC"));

            return $"{qi}{meta.Columns.Keys.First()}{qiClose} ASC";
        }

        // ─────────────────────────────────────────────────────────────────
        //  Query Execution
        // ─────────────────────────────────────────────────────────────────

        protected async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(
            Workspace workspace, string sql, List<DbParameter> parameters, CancellationToken ct)
        {
            var connStr = BuildConnectionString(workspace);
            await using var connection = CreateConnection(connStr);
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 120;
            foreach (var p in parameters) cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await ReadCurrentResultSetAsync(reader, ct);
        }

        protected async Task<int> ExecuteNonQueryAsync(
            Workspace workspace, string sql, List<DbParameter> parameters, CancellationToken ct)
        {
            var connStr = BuildConnectionString(workspace);
            await using var connection = CreateConnection(connStr);
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 120;
            foreach (var p in parameters) cmd.Parameters.Add(p);

            return await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task<T> ExecuteScalarAsync<T>(
            DbConnection connection, string sql, List<DbParameter> parameters, CancellationToken ct) where T : struct
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 120;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is DBNull or null ? default : (T)Convert.ChangeType(result, typeof(T));
        }

        /// <summary>
        /// Reads one full result set from the current position of the reader into rows.
        /// Preserves the legacy behavior of JSON-string flattening for cell values.
        /// </summary>
        protected static async Task<List<Dictionary<string, object?>>> ReadCurrentResultSetAsync(
            DbDataReader reader, CancellationToken ct)
        {
            var result = new List<Dictionary<string, object?>>();

            var schema = await reader.GetColumnSchemaAsync(ct);
            var colNames = schema.Select(c => c.ColumnName ?? "").ToList();

            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < colNames.Count; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        row[colNames[i]] = null;
                    }
                    else
                    {
                        var val = reader.GetValue(i);
                        if (val is string s && (s.TrimStart().StartsWith('[') || s.TrimStart().StartsWith('{')))
                        {
                            try { row[colNames[i]] = JsonSerializer.Deserialize<object>(s); }
                            catch { row[colNames[i]] = s; }
                        }
                        else
                        {
                            row[colNames[i]] = val;
                        }
                    }
                }
                result.Add(row);
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────

        private async Task<Workspace> LoadWorkspaceAsync(string workspaceId, CancellationToken ct)
        {
            var ws = await _workspaceRepository.GetByIdAsync(workspaceId, ct);
            if (string.IsNullOrWhiteSpace(ws.DatabaseName))
                throw new ArgumentException("Workspace has no database associated with it.");
            return ws;
        }

        /// <summary>
        /// Creates an ADO.NET connection for the current dialect using this instance's provider factory.
        /// </summary>
        protected DbConnection CreateConnection(string connStr)
        {
            var factory = GetProviderFactory();
            var conn = factory.CreateConnection() ?? throw new InvalidOperationException("Failed to create database connection.");
            conn.ConnectionString = connStr;
            return conn;
        }

        /// <summary>
        /// Engine-based static connection factory (used by DynamicApiServiceResolver /
        /// DynamicApiMetadataService before the workspace is resolved to a dialect).
        /// </summary>
        internal static DbConnection CreateConnection(DatabaseEngine engine, string connStr)
        {
            var factory = GetProviderFactory(engine);
            var conn = factory.CreateConnection() ?? throw new InvalidOperationException($"Failed to create connection for engine '{engine}'.");
            conn.ConnectionString = connStr;
            return conn;
        }

        /// <summary>
        /// Resolves the ADO.NET <see cref="DbProviderFactory"/> for the given database engine.
        /// Add new engines here by returning their <c>XXClientFactory.Instance</c>.
        /// </summary>
        internal static DbProviderFactory GetProviderFactory(DatabaseEngine engine) => engine switch
        {
            DatabaseEngine.MySql => MySqlClientFactory.Instance,
            _ => SqlClientFactory.Instance,
        };

        /// <summary>
        /// Same-assembly accessor for the protected <see cref="ResolveObjectAsync"/> hook,
        /// so the resolver (not derived from this class) can dispatch object resolution.
        /// </summary>
        internal Task<(string Schema, string Type)> ResolveObjectInternalAsync(
            DbConnection connection, string objectName, CancellationToken ct)
            => ResolveObjectAsync(connection, objectName, ct);

        private static string EscapeLike(string value) =>
            value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"[");

        private static void ValidateColumns(TableMetadata meta, IEnumerable<string> columns)
        {
            foreach (var col in columns)
                if (!meta.Columns.ContainsKey(col))
                    throw new ArgumentException($"Column '{col}' does not exist on table '{meta.TableName}'.");
        }

        /// <summary>
        /// Creates a parameter for the current dialect. Column info is optional; when the
        /// column is a character type the size is set to MAX to avoid truncation.
        /// </summary>
        protected DbParameter CreateParam(string name, object? value, ColumnInfo? colInfo = null)
        {
            var factory = GetProviderFactory();
            var param = factory.CreateParameter() ?? throw new InvalidOperationException("Failed to create parameter.");
            param.ParameterName = name;
            param.Value = ConvertToNativeValue(value);
            if (colInfo != null && colInfo.DataType.IndexOf("char", StringComparison.OrdinalIgnoreCase) >= 0)
                param.Size = -1; // MAX for string types to avoid truncation
            return param;
        }

        /// <summary>
        /// Converts a value to a native .NET type suitable for ADO.NET DbParameter.
        /// System.Text.Json deserializes JSON values as <see cref="JsonElement"/> when the target
        /// type is <c>object?</c> (e.g. <c>Dictionary&lt;string, object?&gt;</c>).
        /// ADO.NET providers do not understand JsonElement, so we must flatten it here.
        /// </summary>
        protected static object ConvertToNativeValue(object? value)
        {
            if (value is null)
                return DBNull.Value;

            if (value is JsonElement json)
            {
                return json.ValueKind switch
                {
                    JsonValueKind.String => (object?)json.GetString() ?? DBNull.Value,
                    JsonValueKind.Number => json.TryGetInt64(out var l) ? l : json.GetDecimal(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => DBNull.Value,
                    JsonValueKind.Object or JsonValueKind.Array => json.GetRawText(),
                    _ => DBNull.Value
                };
            }

            return value;
        }
    }
}