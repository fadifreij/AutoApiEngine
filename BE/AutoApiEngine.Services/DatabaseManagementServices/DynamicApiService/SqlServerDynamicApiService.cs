using System.Data;
using System.Data.Common;
using AutoApiEngine.Domain.Entities;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// SQL Server implementation of <see cref="DynamicApiServiceBase"/>.
    /// Bracketed identifiers, <see cref="SqlClientFactory"/>, INFORMATION_SCHEMA metadata,
    /// OFFSET/FETCH paging, OUTPUT INSERTED.*/DELETED.* write helpers, and EXEC/SELECT
    /// stored-procedure / function execution.
    /// </summary>
    public class SqlServerDynamicApiService : DynamicApiServiceBase
    {
        public SqlServerDynamicApiService(
            IWorkspaceRepository workspaceRepository,
            IForeignKeyService foreignKeyService,
            IConfiguration configuration,
            ILogger<SqlServerDynamicApiService> logger)
            : base(workspaceRepository, foreignKeyService, configuration, logger)
        {
        }

        // ─────────────────────────────────────────────────────────────────
        //  Dialect hooks
        // ─────────────────────────────────────────────────────────────────

        protected override string QuoteIdentifier => "[";

        protected override string QuoteClose => "]";

        protected override DbProviderFactory GetProviderFactory() => SqlClientFactory.Instance;

        protected override string GetColumnsSql() => @"
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

        protected override string GetObjectColumnsSql() => @"
            SELECT COLUMN_NAME,
                   DATA_TYPE + CASE WHEN CHARACTER_MAXIMUM_LENGTH IS NOT NULL
                       THEN '(' + CASE WHEN CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX' ELSE CAST(CHARACTER_MAXIMUM_LENGTH AS VARCHAR) END + ')'
                       ELSE '' END,
                   IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @table AND TABLE_SCHEMA = @schema
            ORDER BY ORDINAL_POSITION";

        protected override string ApplyPaging(string selectSql, int pageSize, int offset)
            => $"{selectSql} OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";

        protected override async Task<(string Schema, string Type)> ResolveObjectAsync(
            DbConnection connection, string objectName, CancellationToken ct)
        {
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @name AND TABLE_TYPE = 'BASE TABLE'";
                cmd.Parameters.Add(CreateParam("@name", objectName));
                var s = await cmd.ExecuteScalarAsync(ct) as string;
                if (s != null) return (s, "TABLE");
            }
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT TABLE_SCHEMA FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = @name";
                cmd.Parameters.Add(CreateParam("@name", objectName));
                var s = await cmd.ExecuteScalarAsync(ct) as string;
                if (s != null) return (s, "VIEW");
            }
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT ROUTINE_SCHEMA, ROUTINE_TYPE FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_NAME = @name";
                cmd.Parameters.Add(CreateParam("@name", objectName));
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                    return (reader.GetString(0), reader.GetString(1).ToUpperInvariant());
            }
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
            var sql = $"INSERT INTO {qi}{meta.Schema}{qc}.{qi}{objectName}{qc} ({quotedColumns}) OUTPUT INSERTED.* VALUES ({paramPlaceholders})";
            var result = await ExecuteQueryAsync(workspace, sql, parameters, ct);
            return result.FirstOrDefault();
        }

        protected override async Task<Dictionary<string, object?>?> UpdateReturnRowAsync(
            Workspace workspace, TableMetadata meta, string objectName, string id, string pkCol,
            string setClauseSql, Dictionary<string, object?> filtered, List<DbParameter> parameters, CancellationToken ct)
        {
            var qi = QuoteIdentifier;
            var qc = QuoteClose;
            var sql = $"UPDATE {qi}{meta.Schema}{qc}.{qi}{objectName}{qc} SET {setClauseSql} OUTPUT INSERTED.* WHERE {qi}{pkCol}{qc} = @pk";
            var result = await ExecuteQueryAsync(workspace, sql, parameters, ct);
            return result.FirstOrDefault();
        }

        protected override async Task<Dictionary<string, object?>?> DeleteReturnRowAsync(
            Workspace workspace, TableMetadata meta, string objectName, string id,
            List<DbParameter> parameters, CancellationToken ct)
        {
            var qi = QuoteIdentifier;
            var qc = QuoteClose;
            var pkCol = meta.PrimaryKeyColumns[0];
            var sql = $"DELETE FROM {qi}{meta.Schema}{qc}.{qi}{objectName}{qc} OUTPUT DELETED.* WHERE {qi}{pkCol}{qc} = @pk";
            var result = await ExecuteQueryAsync(workspace, sql, parameters, ct);
            return result.FirstOrDefault();
        }

        protected internal override string BuildConnectionString(Workspace workspace)
        {
            var dbName = workspace.DatabaseName ?? "";
            var baseConn = Configuration.GetConnectionString("SqlServerConnection")
                ?? "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True;";

            if (!string.IsNullOrWhiteSpace(workspace.DbUserName))
            {
                var csb = new SqlConnectionStringBuilder(baseConn)
                {
                    InitialCatalog = dbName,
                    UserID = workspace.DbUserName,
                    Password = workspace.DbPassword ?? ""
                };
                return csb.ConnectionString;
            }

            var sqlCsb = new SqlConnectionStringBuilder(baseConn) { InitialCatalog = dbName };
            return sqlCsb.ConnectionString;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Stored Procedure / Function execution
        // ─────────────────────────────────────────────────────────────────

        private sealed record RoutineParameterInfo(
            string Name,
            string DataType,
            bool IsOutput,
            bool HasDefault,
            string? DefaultValue,
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

            await using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 120;

            if (string.Equals(type, "FUNCTION", StringComparison.OrdinalIgnoreCase))
            {
                await BuildFunctionCommandAsync(cmd, connection, schema, objectName, qualified, parameters, ct);
                return await ExecuteRoutineAsync(cmd, ct);
            }

            // Stored procedure — EXEC [schema].[proc] @p1 = @in0, @p2 = @in1 OUTPUT ...
            var routineParams = await GetRoutineParameterInfosAsync(connection, schema, objectName, ct);
            var sqlParts = new List<string>();
            var idx = 0;
            foreach (var p in routineParams)
            {
                var pname = $"@in{idx++}";
                var dbParam = CreateParam(pname, GetValueByName(parameters, p.Name));
                dbParam.Direction = p.IsOutput ? ParameterDirection.Output : ParameterDirection.Input;
                if (p.IsOutput && p.DataType.IndexOf("char", StringComparison.OrdinalIgnoreCase) >= 0)
                    dbParam.Size = -1;
                cmd.Parameters.Add(dbParam);

                var segment = $"@{p.Name.TrimStart('@')} = {pname}";
                if (p.IsOutput) segment += " OUTPUT";
                sqlParts.Add(segment);
            }

            cmd.CommandText = sqlParts.Count == 0
                ? $"EXEC {qualified}"
                : $"EXEC {qualified} {string.Join(", ", sqlParts)}";

            return await ExecuteRoutineAsync(cmd, ct);
        }

        private async Task BuildFunctionCommandAsync(
            DbCommand cmd, DbConnection connection, string schema, string objectName, string qualified,
            Dictionary<string, object?> parameters, CancellationToken ct)
        {
            var isTableValued = await IsTableValuedFunctionAsync(connection, schema, objectName, ct);
            var functionParams = await GetRoutineParameterInfosAsync(connection, schema, objectName, ct);

            var sql = isTableValued ? $"SELECT * FROM {qualified}(" : $"SELECT {qualified}(";
            var idx = 0;
            foreach (var p in functionParams)
            {
                var pname = $"@in{idx++}";
                cmd.Parameters.Add(CreateParam(pname, GetValueByName(parameters, p.Name)));
                if (idx == functionParams.Count)
                    sql += $"{pname})";
                else
                    sql += $"{pname}, ";
            }
            if (functionParams.Count == 0)
                sql += ")";

            cmd.CommandText = sql;
        }

        private static async Task<DynamicApiExecutionResponse> ExecuteRoutineAsync(DbCommand cmd, CancellationToken ct)
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

        private async Task<bool> IsTableValuedFunctionAsync(
            DbConnection connection, string schema, string objectName, CancellationToken ct)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT DATA_TYPE
                FROM INFORMATION_SCHEMA.ROUTINES
                WHERE ROUTINE_SCHEMA = @schema AND ROUTINE_NAME = @name AND ROUTINE_TYPE = 'FUNCTION'";
            cmd.Parameters.Add(CreateParam("@schema", schema));
            cmd.Parameters.Add(CreateParam("@name", objectName));
            var dataType = await cmd.ExecuteScalarAsync(ct) as string;
            return string.Equals(dataType, "TABLE", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<List<RoutineParameterInfo>> GetRoutineParameterInfosAsync(
            DbConnection connection, string schema, string objectName, CancellationToken ct)
        {
            var list = new List<RoutineParameterInfo>();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT p.name, t.name, p.is_output, p.has_default_value, p.default_value, p.parameter_id
                FROM sys.parameters p
                JOIN sys.types t ON p.user_type_id = t.user_type_id
                WHERE p.object_id = OBJECT_ID(@qualified)
                  AND p.parameter_id > 0
                ORDER BY p.parameter_id";
            cmd.Parameters.Add(CreateParam("@qualified", $"{schema}.{objectName}"));

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new RoutineParameterInfo(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetBoolean(2),
                    reader.GetBoolean(3),
                    reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString(),
                    reader.GetInt32(5)));
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