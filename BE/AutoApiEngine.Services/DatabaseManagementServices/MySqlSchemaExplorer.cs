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
