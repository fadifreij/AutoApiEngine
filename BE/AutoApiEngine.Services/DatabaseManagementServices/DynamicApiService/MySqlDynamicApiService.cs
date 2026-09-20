using System.Data;
using System.Data.Common;
using AutoApiEngine.Domain.Entities;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// MySQL implementation of <see cref="DynamicApiServiceBase"/>.
    /// Backtick identifiers, <see cref="MySqlClientFactory"/>, information_schema metadata,
    /// LIMIT/OFFSET paging, follow-up SELECT for INSERT/UPDATE return rows,
    /// ExecuteNonQuery for DELETE, and CALL/SELECT routine execution.
    /// </summary>
    public class MySqlDynamicApiService : DynamicApiServiceBase
    {
        public MySqlDynamicApiService(
            IWorkspaceRepository workspaceRepository,
            IForeignKeyService foreignKeyService,
            IConfiguration configuration,
            ILogger<MySqlDynamicApiService> logger)
            : base(workspaceRepository, foreignKeyService, configuration, logger)
        {
        }

        // ─────────────────────────────────────────────────────────────────
        //  Dialect hooks
        // ─────────────────────────────────────────────────────────────────

        protected override string QuoteIdentifier => "`";

        protected override string QuoteClose => "`";

        protected override DbProviderFactory GetProviderFactory() => MySqlClientFactory.Instance;

        protected override string GetColumnsSql() => @"
            SELECT c.COLUMN_NAME, c.COLUMN_TYPE, c.IS_NULLABLE, CASE WHEN c.COLUMN_KEY = 'PRI' THEN 1 ELSE 0 END,
                   CASE WHEN c.EXTRA LIKE '%auto_increment%' THEN 1 ELSE 0 END
            FROM information_schema.COLUMNS c
            WHERE c.TABLE_NAME = @table AND c.TABLE_SCHEMA = @schema
            ORDER BY c.ORDINAL_POSITION";

        protected override string GetObjectColumnsSql() => @"
            SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE
            FROM information_schema.COLUMNS
            WHERE TABLE_NAME = @table AND TABLE_SCHEMA = @schema
            ORDER BY ORDINAL_POSITION";

        protected override string ApplyPaging(string selectSql, int pageSize, int offset)
            => $"{selectSql} LIMIT {pageSize} OFFSET {offset}";

        protected override async Task<(string Schema, string Type)> ResolveObjectAsync(
            DbConnection connection, string objectName, CancellationToken ct)
        {
            const string sql = @"
                SELECT TABLE_SCHEMA, TABLE_TYPE FROM information_schema.tables WHERE TABLE_NAME = @name
                UNION ALL
                SELECT ROUTINE_SCHEMA, ROUTINE_TYPE FROM information_schema.routines WHERE ROUTINE_NAME = @name";
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(CreateParam("@name", objectName));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return (reader.GetString(0), reader.GetString(1).ToUpperInvariant());
            throw new ArgumentException($"Object '{objectName}' not found in the database.");
        }

        protected override async Task<Dictionary<string, ColumnInfo>> GetObjectColumnsAsync(
            DbConnection connection, string schema, string objectName, CancellationToken ct)
        {
            var columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = GetObjectColumnsSql();
            cmd.Parameters.Add(CreateParam("@schema", schema));
            cmd.Parameters.Add(CreateParam("@table", objectName));

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                columns[reader.GetString(0)] = new ColumnInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2) == "YES", false, false);

            return columns;
        }

        protected override async Task<Dictionary<string, object?>?> InsertReturnRowAsync(
            Workspace workspace, TableMetadata meta, string objectName,
            Dictionary<string, object?> filtered, string quotedColumns, string paramPlaceholders,
            List<DbParameter> parameters, CancellationToken ct)
        {
            var qi = QuoteIdentifier;
            var qc = QuoteClose;
            var sql = $"INSERT INTO {qi}{meta.Schema}{qc}.{qi}{objectName}{qc} ({quotedColumns}) VALUES ({paramPlaceholders})";
            await ExecuteQueryAsync(workspace, sql, parameters, ct);

            var pkCol = meta.PrimaryKeyColumns.Count == 1 ? meta.PrimaryKeyColumns[0] : null;
            if (pkCol != null && meta.Columns.TryGetValue(pkCol, out var pkInfo) && pkInfo.IsIdentity)
            {
                // Auto-increment PK — retrieve via LAST_INSERT_ID()
                var selectSql = $"SELECT * FROM {qi}{meta.Schema}{qc}.{qi}{objectName}{qc} WHERE {qi}{pkCol}{qc} = LAST_INSERT_ID()";
                var selectResult = await ExecuteQueryAsync(workspace, selectSql, new List<DbParameter>(), ct);
                return selectResult.FirstOrDefault();
            }
            if (pkCol != null && filtered.ContainsKey(pkCol))
            {
                // Non-identity PK — user provided the value in the request body
                var pkValue = filtered[pkCol];
                var pkParam = CreateParam("@pk", pkValue, meta.Columns[pkCol]);
                var selectSql = $"SELECT * FROM {qi}{meta.Schema}{qc}.{qi}{objectName}{qc} WHERE {qi}{pkCol}{qc} = @pk";
                var selectResult = await ExecuteQueryAsync(workspace, selectSql, new List<DbParameter> { pkParam }, ct);
                return selectResult.FirstOrDefault();
            }
            return null;
        }

        protected override async Task<Dictionary<string, object?>?> UpdateReturnRowAsync(
            Workspace workspace, TableMetadata meta, string objectName, string id, string pkCol,
            string setClauseSql, Dictionary<string, object?> filtered, List<DbParameter> parameters, CancellationToken ct)
        {
            var qi = QuoteIdentifier;
            var qc = QuoteClose;
            var sql = $"UPDATE {qi}{meta.Schema}{qc}.{qi}{objectName}{qc} SET {setClauseSql} WHERE {qi}{pkCol}{qc} = @pk";
            await ExecuteQueryAsync(workspace, sql, parameters, ct);

            var selectSql = $"SELECT * FROM {qi}{meta.Schema}{qc}.{qi}{objectName}{qc} WHERE {qi}{pkCol}{qc} = @pk";
            var selectParams = new List<DbParameter>
            {
                CreateParam("@pk", id, meta.Columns[pkCol])
            };
            var selectResult = await ExecuteQueryAsync(workspace, selectSql, selectParams, ct);
            return selectResult.FirstOrDefault();
        }

        protected override async Task<Dictionary<string, object?>?> DeleteReturnRowAsync(
            Workspace workspace, TableMetadata meta, string objectName, string id,
            List<DbParameter> parameters, CancellationToken ct)
        {
            var qi = QuoteIdentifier;
            var qc = QuoteClose;
            var pkCol = meta.PrimaryKeyColumns[0];
            var sql = $"DELETE FROM {qi}{meta.Schema}{qc}.{qi}{objectName}{qc} WHERE {qi}{pkCol}{qc} = @pk";
            var affected = await ExecuteNonQueryAsync(workspace, sql, parameters, ct);
            return affected == 0 ? null : new Dictionary<string, object?>();
        }

        protected internal override string BuildConnectionString(Workspace workspace)
        {
            var dbName = workspace.DatabaseName ?? "";
            var baseConn = Configuration.GetConnectionString("MySqlConnection")
                ?? "Server=localhost;Port=3307;Uid=root;Pwd=root;";

            if (!string.IsNullOrWhiteSpace(workspace.DbUserName))
                return $"{baseConn.TrimEnd(';')};Database={dbName};Uid={workspace.DbUserName};Pwd={workspace.DbPassword ?? ""}";

            return $"{baseConn.TrimEnd(';')};Database={dbName}";
        }

        // ─────────────────────────────────────────────────────────────────
        //  Stored Procedure / Function execution
        // ─────────────────────────────────────────────────────────────────

        private sealed record RoutineParameterInfo(
            string Name,
            string DataType,
            string ParameterMode,   // "IN" | "OUT" | "INOUT"
            int Ordinal);

        protected override async Task<DynamicApiExecutionResponse> ExecuteRoutineInternalAsync(
            Workspace workspace, string objectName, Dictionary<string, object?> parameters, CancellationToken ct)
        {
            var connStr = BuildConnectionString(workspace);
            await using var connection = CreateConnection(connStr);
            await connection.OpenAsync(ct);

            var (schema, type) = await ResolveObjectAsync(connection, objectName, ct);
            var qi = QuoteIdentifier;
            var qc = QuoteClose;
            var qualified = $"{qi}{schema}{qc}.{qi}{objectName}{qc}";

            var routineParams = await GetRoutineParameterInfosAsync(connection, schema, objectName, ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 120;

            if (string.Equals(type, "FUNCTION", StringComparison.OrdinalIgnoreCase))
            {
                // MySQL functions are scalar only — SELECT fn(@in0, @in1, ...)
                var sql = $"SELECT {qualified}(";
                var idx = 0;
                foreach (var p in routineParams)
                {
                    var pname = $"@in{idx}";
                    cmd.Parameters.Add(CreateParam(pname, GetValueByName(parameters, p.Name)));
                    sql += idx++ == routineParams.Count - 1 ? $"{pname})" : $"{pname}, ";
                }
                cmd.CommandText = routineParams.Count == 0 ? $"SELECT {qualified}()" : sql;
                return await ExecuteRoutineQueryAsync(cmd, ct);
            }

            // Stored procedure — CALL `schema`.`proc`(@in0, @in1, ...)
            var sqlParts2 = new List<string>();
            var idx2 = 0;
            foreach (var p in routineParams)
            {
                var pname = $"@in{idx2}";
                var dbParam = CreateParam(pname, GetValueByName(parameters, p.Name));
                dbParam.Direction = p.ParameterMode.ToUpperInvariant() switch
                {
                    "OUT" => ParameterDirection.Output,
                    "INOUT" => ParameterDirection.InputOutput,
                    _ => ParameterDirection.Input
                };
                if (dbParam.Direction != ParameterDirection.Input && p.DataType.IndexOf("char", StringComparison.OrdinalIgnoreCase) >= 0)
                    dbParam.Size = -1;
                cmd.Parameters.Add(dbParam);
                sqlParts2.Add(pname);
                idx2++;
            }

            cmd.CommandText = sqlParts2.Count == 0
                ? $"CALL {qualified}()"
                : $"CALL {qualified}({string.Join(", ", sqlParts2)})";

            return await ExecuteRoutineQueryAsync(cmd, ct);
        }

        private static async Task<DynamicApiExecutionResponse> ExecuteRoutineQueryAsync(DbCommand cmd, CancellationToken ct)
        {
            var resultSets = new List<Dictionary<string, object?>>();
            int rowsAffected = 0;

            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                do
                {
                    var set = await ReadCurrentResultSetAsync(reader, ct);
                    if (set.Count > 0)
                        resultSets.AddRange(set);
                } while (await reader.NextResultAsync(ct));
                rowsAffected = reader.RecordsAffected;
            }

            var outputParams = new Dictionary<string, object?>();
            foreach (DbParameter p in cmd.Parameters)
            {
                if (p.Direction == ParameterDirection.Output || p.Direction == ParameterDirection.InputOutput)
                    outputParams[p.ParameterName.TrimStart('@')] = p.Value is DBNull ? null : p.Value;
            }

            return new DynamicApiExecutionResponse
            {
                ResultSets = resultSets.Count > 0 ? resultSets : null,
                OutputParams = outputParams.Count > 0 ? outputParams : null,
                RowsAffected = rowsAffected
            };
        }

        private async Task<List<RoutineParameterInfo>> GetRoutineParameterInfosAsync(
            DbConnection connection, string schema, string objectName, CancellationToken ct)
        {
            var list = new List<RoutineParameterInfo>();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT PARAMETER_NAME, DTD_IDENTIFIER, PARAMETER_MODE, ORDINAL_POSITION
                FROM information_schema.PARAMETERS
                WHERE SPECIFIC_SCHEMA = @schema AND SPECIFIC_NAME = @objectName
                  AND PARAMETER_MODE IS NOT NULL
                ORDER BY ORDINAL_POSITION";
            cmd.Parameters.Add(CreateParam("@schema", schema));
            cmd.Parameters.Add(CreateParam("@objectName", objectName));

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new RoutineParameterInfo(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3)));
            }
            return list;
        }

        private static object? GetValueByName(Dictionary<string, object?> parameters, string name)
        {
            foreach (var kvp in parameters)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            return DBNull.Value;
        }
    }
}