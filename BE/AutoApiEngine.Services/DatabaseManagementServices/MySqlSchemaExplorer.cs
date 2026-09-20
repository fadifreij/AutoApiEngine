using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// MySQL implementation of <see cref="ISchemaExplorerService"/>.
    /// Uses information_schema to enumerate tables, views, routines, and columns.
    /// </summary>
    public class MySqlSchemaExplorer : ISchemaExplorerService
    {
        private readonly ILogger<MySqlSchemaExplorer> _logger;
        private readonly string _mySqlConnection;

        public MySqlSchemaExplorer(ILogger<MySqlSchemaExplorer> logger, IConfiguration configuration)
        {
            _logger = logger;
            _mySqlConnection = configuration.GetConnectionString("MySqlConnection")
                ?? "Server=localhost;Port=3307;Uid=root;Pwd=root;";
        }

        public async Task<SchemaExplorerResponse> ExploreAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string? searchFilter = null,
            CancellationToken cancellationToken = default)
        {
            var connStr = string.IsNullOrWhiteSpace(connectionString)
                ? $"{_mySqlConnection};Database={databaseName}"
                : $"{connectionString};Database={databaseName}";

            await using var connection = new MySqlConnection(connStr);
            await connection.OpenAsync(cancellationToken);

            var response = new SchemaExplorerResponse();
            var dbParam = new Dictionary<string, object> { ["@db"] = databaseName };

            // --- Counts ---
            await using (var cmd = BuildCommand(connection,
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @db AND table_type = 'BASE TABLE'", dbParam))
                response.TablesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = BuildCommand(connection,
                "SELECT COUNT(*) FROM information_schema.views WHERE table_schema = @db", dbParam))
                response.ViewsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = BuildCommand(connection,
                "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = @db AND routine_type = 'FUNCTION'", dbParam))
                response.FunctionsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = BuildCommand(connection,
                "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = @db AND routine_type = 'PROCEDURE'", dbParam))
                response.StoredProceduresCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = BuildCommand(connection,
                "SELECT IFNULL(SUM(data_length + index_length), 0) FROM information_schema.tables WHERE table_schema = @db", dbParam))
            {
                var val = await cmd.ExecuteScalarAsync(cancellationToken);
                response.DatabaseSizeBytes = val is DBNull or null ? 0L : Convert.ToInt64(val);
            }

            // --- Objects ---
            var tablesSql = @"
                SELECT t.TABLE_NAME,
                       (SELECT COUNT(*) FROM information_schema.COLUMNS c WHERE c.TABLE_NAME = t.TABLE_NAME AND c.TABLE_SCHEMA = t.TABLE_SCHEMA) AS COLUMN_COUNT
                FROM information_schema.tables t
                WHERE t.TABLE_SCHEMA = @db AND t.TABLE_TYPE = 'BASE TABLE'";

            var viewsSql = @"
                SELECT TABLE_NAME
                FROM information_schema.views
                WHERE TABLE_SCHEMA = @db";

            var routinesSql = @"
                SELECT SPECIFIC_NAME, ROUTINE_TYPE
                FROM information_schema.routines
                WHERE ROUTINE_SCHEMA = @db";

            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                tablesSql += " AND t.TABLE_NAME LIKE @filter";
                viewsSql += " AND TABLE_NAME LIKE @filter";
                routinesSql += " AND SPECIFIC_NAME LIKE @filter";
            }

            // Tables with column counts
            await using (var cmd = new MySqlCommand(tablesSql, connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                if (!string.IsNullOrWhiteSpace(searchFilter))
                    cmd.Parameters.AddWithValue("@filter", $"%{searchFilter}%");

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    response.Objects.Add(new SchemaObjectDto
                    {
                        Name = reader.GetString(0),
                        Type = "Table",
                        ColumnCount = reader.IsDBNull(1) ? null : Convert.ToInt32(reader[1])
                    });
                }
            }

            // Views
            await using (var cmd = new MySqlCommand(viewsSql, connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                if (!string.IsNullOrWhiteSpace(searchFilter))
                    cmd.Parameters.AddWithValue("@filter", $"%{searchFilter}%");

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    response.Objects.Add(new SchemaObjectDto
                    {
                        Name = reader.GetString(0),
                        Type = "View"
                    });
                }
            }

            // Routines (functions + SPs)
            await using (var cmd = new MySqlCommand(routinesSql, connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                if (!string.IsNullOrWhiteSpace(searchFilter))
                    cmd.Parameters.AddWithValue("@filter", $"%{searchFilter}%");

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var routineType = reader.GetString(1);
                    response.Objects.Add(new SchemaObjectDto
                    {
                        Name = reader.GetString(0),
                        Type = routineType == "FUNCTION" ? "Function" : "StoredProcedure"
                    });
                }
            }

            return response;
        }

        public async Task<List<TableSchemaDto>> GetTableColumnsAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            CancellationToken cancellationToken = default)
        {
            var connStr = string.IsNullOrWhiteSpace(connectionString)
                ? $"{_mySqlConnection};Database={databaseName}"
                : $"{connectionString};Database={databaseName}";

            await using var connection = new MySqlConnection(connStr);
            await connection.OpenAsync(cancellationToken);

            const string sql = @"
                SELECT c.TABLE_NAME, c.COLUMN_NAME, c.COLUMN_TYPE, c.IS_NULLABLE, c.COLUMN_KEY
                FROM information_schema.COLUMNS c
                JOIN information_schema.TABLES t
                    ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
                WHERE c.TABLE_SCHEMA = @db AND t.TABLE_TYPE = 'BASE TABLE'
                ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION";

            var tables = new Dictionary<string, TableSchemaDto>(StringComparer.OrdinalIgnoreCase);

            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@db", databaseName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tableName = reader.GetString(0);
                if (!tables.TryGetValue(tableName, out var table))
                {
                    table = new TableSchemaDto { TableName = tableName };
                    tables[tableName] = table;
                }

                table.Columns.Add(new TableColumnDto
                {
                    Name = reader.GetString(1),
                    DataType = reader.GetString(2),
                    IsNullable = string.Equals(reader.GetString(3), "YES", StringComparison.OrdinalIgnoreCase),
                    IsPrimaryKey = string.Equals(reader.GetString(4), "PRI", StringComparison.OrdinalIgnoreCase)
                });
            }

            return tables.Values.ToList();
        }

        public async Task<List<RoutineDefinitionDto>> GetRoutineDefinitionsAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            CancellationToken cancellationToken = default)
        {
            var connStr = string.IsNullOrWhiteSpace(connectionString)
                ? $"{_mySqlConnection};Database={databaseName}"
                : $"{connectionString};Database={databaseName}";

            await using var connection = new MySqlConnection(connStr);
            await connection.OpenAsync(cancellationToken);

            var result = new List<RoutineDefinitionDto>();

            // Views
            await using (var cmd = new MySqlCommand(
                "SELECT TABLE_NAME, VIEW_DEFINITION FROM information_schema.VIEWS WHERE TABLE_SCHEMA = @db", connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.Add(new RoutineDefinitionDto
                    {
                        Name = reader.GetString(0),
                        Type = "View",
                        Definition = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                    });
                }
            }

            // Stored procedures + functions
            await using (var cmd = new MySqlCommand(
                "SELECT ROUTINE_NAME, ROUTINE_TYPE, ROUTINE_DEFINITION FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA = @db", connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var routineType = reader.GetString(1);
                    result.Add(new RoutineDefinitionDto
                    {
                        Name = reader.GetString(0),
                        Type = routineType == "FUNCTION" ? "Function" : "StoredProcedure",
                        Definition = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                    });
                }
            }

            return result;
        }

        public async Task<string> GetObjectDdlAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string objectName,
            string objectType,
            CancellationToken cancellationToken = default)
        {
            var connStr = string.IsNullOrWhiteSpace(connectionString)
                ? $"{_mySqlConnection};Database={databaseName}"
                : $"{connectionString};Database={databaseName}";

            var (showSql, ddlColumn) = objectType switch
            {
                "Table" => ($"SHOW CREATE TABLE `{objectName.Replace("`", "``")}`", "Create Table"),
                "View" => ($"SHOW CREATE VIEW `{objectName.Replace("`", "``")}`", "Create View"),
                "StoredProcedure" => ($"SHOW CREATE PROCEDURE `{objectName.Replace("`", "``")}`", "Create Procedure"),
                "Function" => ($"SHOW CREATE FUNCTION `{objectName.Replace("`", "``")}`", "Create Function"),
                _ => throw new ArgumentException($"Unsupported object type: {objectType}")
            };

            await using var connection = new MySqlConnection(connStr);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = new MySqlCommand(showSql, connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                // Read the DDL by column name rather than a positional index. The column
                // order differs by object type (e.g. SHOW CREATE PROCEDURE/FUNCTION return
                // an extra `sql_mode` column before the DDL), so an index-based lookup can
                // accidentally return `sql_mode` instead of the actual CREATE statement.
                var ddlOrdinal = reader.GetOrdinal(ddlColumn);
                return reader.IsDBNull(ddlOrdinal) ? string.Empty : reader.GetString(ddlOrdinal);
            }

            return string.Empty;
        }

        /// <summary>
        /// Retrieves the declared parameters of a stored procedure or function from
        /// information_schema.PARAMETERS. Views have no parameter rows, so they
        /// naturally return an empty list. The first row of a function is the RETURN
        /// row whose PARAMETER_NAME is NULL — rows with a null/empty name are skipped.
        /// MySQL exposes no default-value metadata, so HasDefault/DefaultValue are stubbed.
        /// </summary>
        public async Task<List<RoutineParameterDto>> GetRoutineParametersAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string schema,
            string objectName,
            CancellationToken cancellationToken = default)
        {
            var connStr = string.IsNullOrWhiteSpace(connectionString)
                ? $"{_mySqlConnection};Database={databaseName}"
                : $"{connectionString};Database={databaseName}";

            await using var connection = new MySqlConnection(connStr);
            await connection.OpenAsync(cancellationToken);

            const string sql = @"
                SELECT PARAMETER_NAME AS Name,
                       DTD_IDENTIFIER AS DataType,
                       COALESCE(PARAMETER_MODE, 'IN') AS Mode,
                       ORDINAL_POSITION AS OrdinalPosition
                FROM information_schema.PARAMETERS
                WHERE SPECIFIC_SCHEMA = @Schema AND SPECIFIC_NAME = @ObjectName
                ORDER BY ORDINAL_POSITION;";

            var result = new List<RoutineParameterDto>();

            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Schema", schema);
            cmd.Parameters.AddWithValue("@ObjectName", objectName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // Skip the RETURN row of functions (PARAMETER_NAME is NULL for it).
                var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                result.Add(new RoutineParameterDto
                {
                    Name = name,
                    DataType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ParameterMode = reader.IsDBNull(2) ? "IN" : reader.GetString(2),
                    HasDefault = false,
                    DefaultValue = null,
                    OrdinalPosition = reader.GetInt32(3)
                });
            }

            return result;
        }

        /// <summary>
        /// Returns the SQL definition (body) of a single stored procedure from
        /// information_schema.ROUTINES. Empty string when unavailable.
        /// </summary>
        public async Task<string> GetRoutineDefinitionAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string schema,
            string objectName,
            CancellationToken cancellationToken = default)
        {
            var connStr = string.IsNullOrWhiteSpace(connectionString)
                ? $"{_mySqlConnection};Database={databaseName}"
                : $"{connectionString};Database={databaseName}";

            await using var connection = new MySqlConnection(connStr);
            await connection.OpenAsync(cancellationToken);

            const string sql = @"
                SELECT ROUTINE_DEFINITION FROM information_schema.ROUTINES
                WHERE ROUTINE_SCHEMA = @Schema AND ROUTINE_NAME = @ObjectName;";

            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Schema", schema);
            cmd.Parameters.AddWithValue("@ObjectName", objectName);

            return (await cmd.ExecuteScalarAsync(cancellationToken) as string) ?? string.Empty;
        }

        private static MySqlCommand BuildCommand(MySqlConnection connection, string sql, Dictionary<string, object>? parameters)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            if (parameters != null)
            {
                foreach (var kvp in parameters)
                {
                    var param = cmd.CreateParameter();
                    param.ParameterName = kvp.Key;
                    param.Value = kvp.Value;
                    cmd.Parameters.Add(param);
                }
            }
            return cmd;
        }
    }
}
