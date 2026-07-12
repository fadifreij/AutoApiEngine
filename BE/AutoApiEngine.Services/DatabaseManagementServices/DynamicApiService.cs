using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Core implementation of <see cref="IDynamicApiService"/>.
    /// Dynamically builds and executes parameterized SQL for any database object,
    /// supporting filtering, sorting, paging, and related-table expansion.
    /// </summary>
    public class DynamicApiService : IDynamicApiService
    {
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IForeignKeyService _foreignKeyService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DynamicApiService> _logger;

        private const int MaxInValues = 100;
        private const int MaxPageSize = 1000;
        private const int DefaultPageSize = 100;

        public DynamicApiService(
            IWorkspaceRepository workspaceRepository,
            IForeignKeyService foreignKeyService,
            IConfiguration configuration,
            ILogger<DynamicApiService> logger)
        {
            _workspaceRepository = workspaceRepository;
            _foreignKeyService = foreignKeyService;
            _configuration = configuration;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        public async Task<DynamicApiListResponse> GetListAsync(
            string workspaceId,
            string objectName,
            DynamicApiQueryRequest query,
            CancellationToken cancellationToken = default)
        {
            var (workspace, meta) = await LoadWorkspaceAndMetaAsync(workspaceId, objectName, cancellationToken);

            var (sql, parameters) = BuildSelectQuery(meta, query, includePaging: true, workspace.DatabaseEngine);
            _logger.LogDebug("Dynamic GET list: {Sql}", sql);
            var data = await ExecuteQueryAsync(workspace, sql, parameters, cancellationToken);

            var response = new DynamicApiListResponse { Data = data };

            if (query.PageSize > 0)
            {
                var (countSql, countParams) = BuildCountQuery(meta, query, workspace.DatabaseEngine);
                var connStr = BuildConnectionString(workspace);
                await using var conn = CreateConnection(workspace.DatabaseEngine, connStr);
                await conn.OpenAsync(cancellationToken);
                var totalCount = await ExecuteScalarAsync<long>(conn, countSql, countParams, cancellationToken);
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

        public async Task<DynamicApiSingleResponse> GetByIdAsync(
            string workspaceId,
            string objectName,
            string id,
            DynamicApiQueryRequest? query = null,
            CancellationToken cancellationToken = default)
        {
            var (workspace, meta) = await LoadWorkspaceAndMetaAsync(workspaceId, objectName, cancellationToken);

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

                var (sql, parameters) = BuildSelectQuery(meta, query, includePaging: false, workspace.DatabaseEngine);
                _logger.LogDebug("Dynamic GET by ID: {Sql}", sql);
                var data = await ExecuteQueryAsync(workspace, sql, parameters, cancellationToken);

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

        public async Task<DynamicApiSingleResponse> GetByCompositeKeyAsync(
            string workspaceId,
            string objectName,
            Dictionary<string, string> pkValues,
            DynamicApiQueryRequest? query = null,
            CancellationToken cancellationToken = default)
        {
            var (workspace, meta) = await LoadWorkspaceAndMetaAsync(workspaceId, objectName, cancellationToken);

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
                var (sql, parameters) = BuildSelectQuery(meta, query, includePaging: false, workspace.DatabaseEngine);
                _logger.LogDebug("Dynamic GET by composite key: {Sql}", sql);
                var data = await ExecuteQueryAsync(workspace, sql, parameters, cancellationToken);

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

        public async Task<DynamicApiActionResponse> CreateAsync(
            string workspaceId,
            string objectName,
            Dictionary<string, object?> data,
            CancellationToken cancellationToken = default)
        {
            var (workspace, meta) = await LoadWorkspaceAndMetaAsync(workspaceId, objectName, cancellationToken);

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

            var qi = QuoteIdentifier(workspace.DatabaseEngine);
            var qiClose = qi == "[" ? "]" : qi;
            var columns = string.Join(", ", filtered.Keys.Select(c => $"{qi}{c}{qiClose}"));
            var paramNames = string.Join(", ", filtered.Keys.Select((c, i) => $"@p{i}"));
            var parameters = new List<DbParameter>();
            var idx = 0;
            foreach (var kvp in filtered)
            {
                parameters.Add(CreateParam($"@p{idx}", kvp.Value, meta.Columns[kvp.Key], workspace.DatabaseEngine));
                idx++;
            }

            var isMySql = workspace.DatabaseEngine == DatabaseEngine.MySql;
            var sql = new StringBuilder();
            sql.Append($"INSERT INTO {qi}{meta.Schema}{qiClose}.{qi}{objectName}{qiClose} ({columns})");
            if (!isMySql)
                sql.Append($" OUTPUT INSERTED.*");
            sql.Append($" VALUES ({paramNames})");

            _logger.LogDebug("Dynamic INSERT: {Sql}", sql);

            Dictionary<string, object?>? inserted;
            if (isMySql)
            {
                // MySQL does not support OUTPUT clause.
                // Execute INSERT first, then SELECT the inserted row.
                await ExecuteQueryAsync(workspace, sql.ToString(), parameters, cancellationToken);

                var pkCol = meta.PrimaryKeyColumns.Count == 1 ? meta.PrimaryKeyColumns[0] : null;
                if (pkCol != null && meta.Columns.TryGetValue(pkCol, out var pkInfo) && pkInfo.IsIdentity)
                {
                    // Auto-increment PK — retrieve via LAST_INSERT_ID()
                    var selectSql = $"SELECT * FROM {qi}{meta.Schema}{qiClose}.{qi}{objectName}{qiClose} WHERE {qi}{pkCol}{qiClose} = LAST_INSERT_ID()";
                    var selectResult = await ExecuteQueryAsync(workspace, selectSql, new List<DbParameter>(), cancellationToken);
                    inserted = selectResult.FirstOrDefault();
                }
                else if (pkCol != null && filtered.ContainsKey(pkCol))
                {
                    // Non-identity PK — user provided the value in the request body
                    var pkValue = filtered[pkCol];
                    var pkParam = CreateParam("@pk", pkValue, meta.Columns[pkCol], workspace.DatabaseEngine);
                    var selectSql = $"SELECT * FROM {qi}{meta.Schema}{qiClose}.{qi}{objectName}{qiClose} WHERE {qi}{pkCol}{qiClose} = @pk";
                    var selectResult = await ExecuteQueryAsync(workspace, selectSql, new List<DbParameter> { pkParam }, cancellationToken);
                    inserted = selectResult.FirstOrDefault();
                }
                else
                {
                    inserted = null;
                }
            }
            else
            {
                var result = await ExecuteQueryAsync(workspace, sql.ToString(), parameters, cancellationToken);
                inserted = result.FirstOrDefault();
            }

            return new DynamicApiActionResponse
            {
                Data = inserted,
                Message = "Record created successfully."
            };
        }

        public async Task<DynamicApiActionResponse> UpdateAsync(
            string workspaceId,
            string objectName,
            string id,
            Dictionary<string, object?> data,
            CancellationToken cancellationToken = default)
        {
            var (workspace, meta) = await LoadWorkspaceAndMetaAsync(workspaceId, objectName, cancellationToken);

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
            var qi = QuoteIdentifier(workspace.DatabaseEngine);
            var qiClose = qi == "[" ? "]" : qi;

            var setClauses = filtered.Keys.Select((c, i) => $"{qi}{c}{qiClose} = @p{i}");
            var parameters = new List<DbParameter>();
            var idx = 0;
            foreach (var kvp in filtered)
            {
                parameters.Add(CreateParam($"@p{idx}", kvp.Value, meta.Columns[kvp.Key], workspace.DatabaseEngine));
                idx++;
            }
            parameters.Add(CreateParam("@pk", id, meta.Columns[pkCol], workspace.DatabaseEngine));

            var isMySql = workspace.DatabaseEngine == DatabaseEngine.MySql;
            var sql = new StringBuilder();
            sql.Append($"UPDATE {qi}{meta.Schema}{qiClose}.{qi}{objectName}{qiClose}");
            sql.Append($" SET {string.Join(", ", setClauses)}");
            if (!isMySql)
                sql.Append($" OUTPUT INSERTED.*");
            sql.Append($" WHERE {qi}{pkCol}{qiClose} = @pk");

            _logger.LogDebug("Dynamic UPDATE: {Sql}", sql);

            Dictionary<string, object?>? updated;
            if (isMySql)
            {
                // MySQL does not support OUTPUT clause.
                // Execute UPDATE first, then SELECT the updated row.
                await ExecuteQueryAsync(workspace, sql.ToString(), parameters, cancellationToken);

                var selectSql = $"SELECT * FROM {qi}{meta.Schema}{qiClose}.{qi}{objectName}{qiClose} WHERE {qi}{pkCol}{qiClose} = @pk";
                var selectParams = new List<DbParameter>
                {
                    CreateParam("@pk", id, meta.Columns[pkCol], workspace.DatabaseEngine)
                };
                var selectResult = await ExecuteQueryAsync(workspace, selectSql, selectParams, cancellationToken);
                updated = selectResult.FirstOrDefault();
            }
            else
            {
                var result = await ExecuteQueryAsync(workspace, sql.ToString(), parameters, cancellationToken);
                updated = result.FirstOrDefault();
            }

            if (updated == null)
                throw new KeyNotFoundException($"Record with {pkCol} = '{id}' not found in '{objectName}'.");

            return new DynamicApiActionResponse
            {
                Data = updated,
                Message = "Record updated successfully."
            };
        }

        public async Task<DynamicApiActionResponse> DeleteAsync(
            string workspaceId,
            string objectName,
            string id,
            CancellationToken cancellationToken = default)
        {
            var (workspace, meta) = await LoadWorkspaceAndMetaAsync(workspaceId, objectName, cancellationToken);

            if (meta.PrimaryKeyColumns.Count != 1)
                throw new ArgumentException(
                    $"Table '{objectName}' has {meta.PrimaryKeyColumns.Count} PK columns. " +
                    "Use query parameters for composite key deletes.");

            var pkCol = meta.PrimaryKeyColumns[0];
            var qi = QuoteIdentifier(workspace.DatabaseEngine);
            var qiClose = qi == "[" ? "]" : qi;
            var parameters = new List<DbParameter>
            {
                CreateParam("@pk", id, meta.Columns[pkCol], workspace.DatabaseEngine)
            };

            var isMySql = workspace.DatabaseEngine == DatabaseEngine.MySql;
            var sql = isMySql
                ? $"DELETE FROM {qi}{meta.Schema}{qiClose}.{qi}{objectName}{qiClose} WHERE {qi}{pkCol}{qiClose} = @pk"
                : $"DELETE FROM {qi}{meta.Schema}{qiClose}.{qi}{objectName}{qiClose} OUTPUT DELETED.* WHERE {qi}{pkCol}{qiClose} = @pk";

            _logger.LogDebug("Dynamic DELETE: {Sql}", sql);

            if (isMySql)
            {
                // MySQL does not support OUTPUT clause.
                // Use ExecuteNonQuery to get affected rows count.
                var affected = await ExecuteNonQueryAsync(workspace, sql, parameters, cancellationToken);
                if (affected == 0)
                    throw new KeyNotFoundException($"Record with {pkCol} = '{id}' not found in '{objectName}'.");
            }
            else
            {
                var result = await ExecuteQueryAsync(workspace, sql, parameters, cancellationToken);
                var deleted = result.FirstOrDefault();
                if (deleted == null)
                    throw new KeyNotFoundException($"Record with {pkCol} = '{id}' not found in '{objectName}'.");
            }

            return new DynamicApiActionResponse
            {
                Message = "Record deleted successfully."
            };
        }

        // ─────────────────────────────────────────────────────────────────
        //  Table Metadata
        // ─────────────────────────────────────────────────────────────────

        internal record ColumnInfo(string Name, string DataType, bool IsNullable, bool IsPrimaryKey, bool IsIdentity);

        internal record TableMetadata(
            string Schema,
            string TableName,
            Dictionary<string, ColumnInfo> Columns,
            List<string> PrimaryKeyColumns
        );

        private async Task<(Workspace Workspace, TableMetadata Meta)> LoadWorkspaceAndMetaAsync(
            string workspaceId, string objectName, CancellationToken ct)
        {
            var workspace = await LoadWorkspaceAsync(workspaceId, ct);
            var meta = await GetTableMetadataAsync(workspace, objectName, ct);
            return (workspace, meta);
        }

        internal async Task<TableMetadata> GetTableMetadataAsync(Workspace workspace, string objectName, CancellationToken ct)
        {
            var connStr = BuildConnectionString(workspace);
            await using var connection = CreateConnection(workspace.DatabaseEngine, connStr);
            await connection.OpenAsync(ct);

            var (schema, objectType) = await ResolveObjectAsync(connection, workspace.DatabaseEngine, objectName, ct);

            if (objectType != "TABLE" && objectType != "BASE TABLE")
            {
                var viewCols = await GetObjectColumnsAsync(connection, workspace.DatabaseEngine, schema, objectName, ct);
                return new TableMetadata(schema, objectName, viewCols, new List<string>());
            }

            var columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
            var pkColumns = new List<string>();
            var colSql = workspace.DatabaseEngine == DatabaseEngine.MySql
                ? GetMySqlColumnsSql()
                : GetSqlColumnsSql();

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = colSql;
                cmd.Parameters.Add(CreateParam("@schema", schema, engine: workspace.DatabaseEngine));
                cmd.Parameters.Add(CreateParam("@table", objectName, engine: workspace.DatabaseEngine));

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

        private static string GetSqlColumnsSql() => @"
            SELECT c.COLUMN_NAME,
                   c.DATA_TYPE + CASE WHEN c.CHARACTER_MAXIMUM_LENGTH IS NOT NULL
                       THEN '(' + CASE WHEN c.CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX' ELSE CAST(c.CHARACTER_MAXIMUM_LENGTH AS VARCHAR) END + ')'
                       WHEN c.NUMERIC_PRECISION IS NOT NULL AND c.NUMERIC_SCALE > 0 THEN '(' + CAST(c.NUMERIC_PRECISION AS VARCHAR) + ',' + CAST(c.NUMERIC_SCALE AS VARCHAR) + ')'
                       WHEN c.NUMERIC_PRECISION IS NOT NULL THEN '(' + CAST(c.NUMERIC_PRECISION AS VARCHAR) + ')'
                       ELSE '' END,
                   c.IS_NULLABLE,
                   CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END,
                   ISNULL(COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity'), 0)
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (SELECT ku.TABLE_NAME, ku.COLUMN_NAME, ku.TABLE_SCHEMA
                       FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                       JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                       WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY') pk
                ON pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME AND pk.TABLE_SCHEMA = c.TABLE_SCHEMA
            WHERE c.TABLE_NAME = @table AND c.TABLE_SCHEMA = @schema
            ORDER BY c.ORDINAL_POSITION";

        private static string GetMySqlColumnsSql() => @"
            SELECT c.COLUMN_NAME, c.COLUMN_TYPE, c.IS_NULLABLE, CASE WHEN c.COLUMN_KEY = 'PRI' THEN 1 ELSE 0 END,
                   CASE WHEN c.EXTRA LIKE '%auto_increment%' THEN 1 ELSE 0 END
            FROM information_schema.COLUMNS c
            WHERE c.TABLE_NAME = @table AND c.TABLE_SCHEMA = @schema
            ORDER BY c.ORDINAL_POSITION";

        private static async Task<Dictionary<string, ColumnInfo>> GetObjectColumnsAsync(
            DbConnection connection, DatabaseEngine engine, string schema, string objectName, CancellationToken ct)
        {
            var columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
            var sql = engine == DatabaseEngine.MySql
                ? @"SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_NAME = @table AND TABLE_SCHEMA = @schema ORDER BY ORDINAL_POSITION"
                : @"SELECT COLUMN_NAME, DATA_TYPE + CASE WHEN CHARACTER_MAXIMUM_LENGTH IS NOT NULL THEN '(' + CASE WHEN CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX' ELSE CAST(CHARACTER_MAXIMUM_LENGTH AS VARCHAR) END + ')' ELSE '' END, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND TABLE_SCHEMA = @schema ORDER BY ORDINAL_POSITION";

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(CreateParam("@schema", schema, engine: engine));
            cmd.Parameters.Add(CreateParam("@table", objectName, engine: engine));

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                columns[reader.GetString(0)] = new ColumnInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2) == "YES", false, false);

            return columns;
        }

        internal static async Task<(string Schema, string Type)> ResolveObjectStatic(
            DbConnection connection, DatabaseEngine engine, string objectName, CancellationToken ct)
        {
            return await ResolveObjectAsync(connection, engine, objectName, ct);
        }

        private static async Task<(string Schema, string Type)> ResolveObjectAsync(
            DbConnection connection, DatabaseEngine engine, string objectName, CancellationToken ct)
        {
            if (engine == DatabaseEngine.MySql)
            {
                const string sql = @"
                    SELECT TABLE_SCHEMA, TABLE_TYPE FROM information_schema.tables WHERE TABLE_NAME = @name
                    UNION ALL
                    SELECT ROUTINE_SCHEMA, ROUTINE_TYPE FROM information_schema.routines WHERE ROUTINE_NAME = @name";
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.Add(CreateParam("@name", objectName, engine: engine));
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                    return (reader.GetString(0), reader.GetString(1).ToUpperInvariant());
            }
            else
            {
                await using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @name AND TABLE_TYPE = 'BASE TABLE'";
                    cmd.Parameters.Add(CreateParam("@name", objectName, engine: engine));
                    var s = await cmd.ExecuteScalarAsync(ct) as string;
                    if (s != null) return (s, "TABLE");
                }
                await using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT TABLE_SCHEMA FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = @name";
                    cmd.Parameters.Add(CreateParam("@name", objectName, engine: engine));
                    var s = await cmd.ExecuteScalarAsync(ct) as string;
                    if (s != null) return (s, "VIEW");
                }
                await using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT ROUTINE_SCHEMA, ROUTINE_TYPE FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_NAME = @name";
                    cmd.Parameters.Add(CreateParam("@name", objectName, engine: engine));
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    if (await reader.ReadAsync(ct))
                        return (reader.GetString(0), reader.GetString(1).ToUpperInvariant());
                }
            }
            throw new ArgumentException($"Object '{objectName}' not found in the database.");
        }

        // ─────────────────────────────────────────────────────────────────
        //  SQL Builders
        // ─────────────────────────────────────────────────────────────────

        private (string Sql, List<DbParameter> Parameters) BuildSelectQuery(
            TableMetadata meta, DynamicApiQueryRequest query, bool includePaging, DatabaseEngine engine)
        {
            var parameters = new List<DbParameter>();
            var qi = QuoteIdentifier(engine);
            var sb = new StringBuilder();

            // SELECT
            sb.Append("SELECT ");
            var selectStr = query.Select is { Count: > 0 } ? string.Join(",", query.Select) : null;
            var selectItems = BuildSelectList(meta, selectStr, qi);
            sb.Append(string.Join(", ", selectItems));

            sb.Append($" FROM {qi}{meta.Schema}{(qi == "[" ? "]" : qi)}.{qi}{meta.TableName}{(qi == "[" ? "]" : qi)}");

            // WHERE
            if (query.Filter != null && query.Filter.Count > 0)
            {
                var (whereSql, filterParams) = BuildWhereClause(meta, query.Filter, engine);
                if (!string.IsNullOrWhiteSpace(whereSql))
                {
                    sb.Append($" WHERE {whereSql}");
                    parameters.AddRange(filterParams);
                }
            }

            // ORDER BY
            var sortStr = query.Sort is { Count: > 0 } ? string.Join(",", query.Sort) : null;
            sb.Append($" ORDER BY {BuildOrderByClause(meta, sortStr, engine)}");

            // OFFSET/FETCH or LIMIT
            if (includePaging && query.PageSize > 0)
            {
                var offset = Math.Max(0, (query.Page - 1) * query.PageSize);
                if (engine == DatabaseEngine.MySql)
                    sb.Append($" LIMIT {query.PageSize} OFFSET {offset}");
                else
                    sb.Append($" OFFSET {offset} ROWS FETCH NEXT {query.PageSize} ROWS ONLY");
            }

            return (sb.ToString(), parameters);
        }

        private (string Sql, List<DbParameter> Parameters) BuildCountQuery(
            TableMetadata meta, DynamicApiQueryRequest query, DatabaseEngine engine)
        {
            var parameters = new List<DbParameter>();
            var qi = QuoteIdentifier(engine);
            var qiClose = qi == "[" ? "]" : qi;
            var sb = new StringBuilder();

            sb.Append($"SELECT COUNT(*) FROM {qi}{meta.Schema}{qiClose}.{qi}{meta.TableName}{qiClose}");

            if (query.Filter != null && query.Filter.Count > 0)
            {
                var (whereSql, filterParams) = BuildWhereClause(meta, query.Filter, engine);
                if (!string.IsNullOrWhiteSpace(whereSql))
                {
                    sb.Append($" WHERE {whereSql}");
                    parameters.AddRange(filterParams);
                }
            }

            return (sb.ToString(), parameters);
        }

        private static List<string> BuildSelectList(TableMetadata meta, string? select, string qi)
        {
            if (string.IsNullOrWhiteSpace(select))
                return meta.Columns.Keys.Select(c => $"{qi}{c}{(qi == "[" ? "]" : qi)}").ToList();

            var qiClose = qi == "[" ? "]" : qi;
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
            TableMetadata meta, List<string> filters, DatabaseEngine engine)
        {
            var andClauses = new List<string>();
            var orClauses = new List<string>();
            var parameters = new List<DbParameter>();
            var pIdx = 0;
            var qi = QuoteIdentifier(engine);
            var qiClose = qi == "[" ? "]" : qi;

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
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column], engine)); pIdx++;
                        break;
                    case "neq":
                        clause = $"{colRef} <> @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column], engine)); pIdx++;
                        break;
                    case "gt":
                        clause = $"{colRef} > @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column], engine)); pIdx++;
                        break;
                    case "gte":
                        clause = $"{colRef} >= @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column], engine)); pIdx++;
                        break;
                    case "lt":
                        clause = $"{colRef} < @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column], engine)); pIdx++;
                        break;
                    case "lte":
                        clause = $"{colRef} <= @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", value, meta.Columns[column], engine)); pIdx++;
                        break;
                    case "contains":
                        clause = $"{colRef} LIKE @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", $"%{EscapeLike(value ?? "")}%", meta.Columns[column], engine)); pIdx++;
                        break;
                    case "startswith":
                        clause = $"{colRef} LIKE @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", $"{EscapeLike(value ?? "")}%", meta.Columns[column], engine)); pIdx++;
                        break;
                    case "endswith":
                        clause = $"{colRef} LIKE @p{pIdx}";
                        parameters.Add(CreateParam($"@p{pIdx}", $"%{EscapeLike(value ?? "")}", meta.Columns[column], engine)); pIdx++;
                        break;
                    case "in":
                        var vals = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (vals.Length > MaxInValues) throw new ArgumentException($"IN exceeds max {MaxInValues} values.");
                        if (vals.Length == 0) throw new ArgumentException("IN requires at least one value.");
                        var phs = string.Join(", ", vals.Select((_, i) => $"@p{pIdx + i}"));
                        for (int i = 0; i < vals.Length; i++)
                            parameters.Add(CreateParam($"@p{pIdx + i}", vals[i], meta.Columns[column], engine));
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

        private string BuildOrderByClause(TableMetadata meta, string? sort, DatabaseEngine engine)
        {
            var qi = QuoteIdentifier(engine);
            var qiClose = qi == "[" ? "]" : qi;

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

        private async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(
            Workspace workspace, string sql, List<DbParameter> parameters, CancellationToken ct)
        {
            var connStr = BuildConnectionString(workspace);
            await using var connection = CreateConnection(workspace.DatabaseEngine, connStr);
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 120;
            foreach (var p in parameters) cmd.Parameters.Add(p);

            var result = new List<Dictionary<string, object?>>();

            await using var reader = await cmd.ExecuteReaderAsync(ct);
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

        private async Task<int> ExecuteNonQueryAsync(
            Workspace workspace, string sql, List<DbParameter> parameters, CancellationToken ct)
        {
            var connStr = BuildConnectionString(workspace);
            await using var connection = CreateConnection(workspace.DatabaseEngine, connStr);
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

        internal string BuildConnectionString(Workspace workspace)
        {
            var engine = workspace.DatabaseEngine;
            var dbName = workspace.DatabaseName ?? "";
            var baseConn = engine switch
            {
                DatabaseEngine.MySql => _configuration.GetConnectionString("MySqlConnection") ?? "Server=localhost;Port=3307;Uid=root;Pwd=root;",
                _ => _configuration.GetConnectionString("SqlServerConnection") ?? "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True;"
            };

            if (!string.IsNullOrWhiteSpace(workspace.DbUserName))
            {
                if (engine == DatabaseEngine.MySql)
                    return $"{baseConn.TrimEnd(';')};Database={dbName};Uid={workspace.DbUserName};Pwd={workspace.DbPassword ?? ""}";

                var csb = new SqlConnectionStringBuilder(baseConn) { InitialCatalog = dbName, UserID = workspace.DbUserName, Password = workspace.DbPassword ?? "" };
                return csb.ConnectionString;
            }

            if (engine == DatabaseEngine.MySql)
                return $"{baseConn.TrimEnd(';')};Database={dbName}";

            var sqlCsb = new SqlConnectionStringBuilder(baseConn) { InitialCatalog = dbName };
            return sqlCsb.ConnectionString;
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

        internal static DbConnection CreateConnection(DatabaseEngine engine, string connStr)
        {
            var factory = GetProviderFactory(engine);
            var conn = factory.CreateConnection() ?? throw new InvalidOperationException($"Failed to create connection for engine '{engine}'.");
            conn.ConnectionString = connStr;
            return conn;
        }

        internal static string QuoteIdentifier(DatabaseEngine engine) => engine == DatabaseEngine.MySql ? "`" : "[";

        private static string EscapeLike(string value) =>
            value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"[");

        private static void ValidateColumns(TableMetadata meta, IEnumerable<string> columns)
        {
            foreach (var col in columns)
                if (!meta.Columns.ContainsKey(col))
                    throw new ArgumentException($"Column '{col}' does not exist on table '{meta.TableName}'.");
        }

        internal static DbParameter CreateParam(string name, object? value, ColumnInfo? colInfo = null, DatabaseEngine engine = DatabaseEngine.SqlServer)
        {
            var factory = GetProviderFactory(engine);
            var param = factory.CreateParameter() ?? throw new InvalidOperationException($"Failed to create parameter for engine '{engine}'.");
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
        internal static object ConvertToNativeValue(object? value)
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
