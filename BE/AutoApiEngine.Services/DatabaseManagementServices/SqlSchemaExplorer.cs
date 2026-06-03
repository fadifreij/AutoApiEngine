using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// SQL Server implementation of <see cref="ISchemaExplorerService"/>.
    /// Uses INFORMATION_SCHEMA views and sys.dm_* DMFs to enumerate objects and columns.
    /// </summary>
    public class SqlSchemaExplorer : ISchemaExplorerService
    {
        private readonly ILogger<SqlSchemaExplorer> _logger;
        private readonly string _appConnectionString;

        public SqlSchemaExplorer(ILogger<SqlSchemaExplorer> logger, IConfiguration configuration)
        {
            _logger = logger;
            _appConnectionString = configuration.GetConnectionString("SqlServerConnection")
                ?? throw new InvalidOperationException("SqlServerConnection is not configured.");
        }

        public async Task<SchemaExplorerResponse> ExploreAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string? searchFilter = null,
            CancellationToken cancellationToken = default)
        {
            var targetConn = string.IsNullOrWhiteSpace(connectionString) ? _appConnectionString : connectionString;
            var csb = new SqlConnectionStringBuilder(targetConn)
            {
                InitialCatalog = databaseName
            };

            await using var connection = new SqlConnection(csb.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var response = new SchemaExplorerResponse();

            // --- Counts ---
            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'", connection))
                response.TablesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.VIEWS", connection))
                response.ViewsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'FUNCTION'", connection))
                response.FunctionsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE'", connection))
                response.StoredProceduresCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = new SqlCommand("SELECT ISNULL(SUM(CAST(size AS BIGINT) * 8 * 1024), 0) FROM sys.database_files WHERE type = 0", connection))
            {
                var val = await cmd.ExecuteScalarAsync(cancellationToken);
                response.DatabaseSizeBytes = val is DBNull or null ? 0L : Convert.ToInt64(val);
            }

            // --- Objects ---
            var tablesSql = @"
                SELECT 
                    t.TABLE_NAME,
                    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME = t.TABLE_NAME AND c.TABLE_SCHEMA = t.TABLE_SCHEMA) AS COLUMN_COUNT
                FROM INFORMATION_SCHEMA.TABLES t
                WHERE t.TABLE_TYPE = 'BASE TABLE'";

            var viewsSql = @"
                SELECT TABLE_NAME
                FROM INFORMATION_SCHEMA.VIEWS";

            var routinesSql = @"
                SELECT SPECIFIC_NAME, ROUTINE_TYPE
                FROM INFORMATION_SCHEMA.ROUTINES";

            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                tablesSql += $" AND t.TABLE_NAME LIKE @filter";
                viewsSql += $" WHERE TABLE_NAME LIKE @filter";
                routinesSql += $" WHERE SPECIFIC_NAME LIKE @filter";
            }

            // Tables with column counts
            await using (var cmd = new SqlCommand(tablesSql, connection))
            {
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
            await using (var cmd = new SqlCommand(viewsSql, connection))
            {
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

            // Routines (functions + stored procedures)
            await using (var cmd = new SqlCommand(routinesSql, connection))
            {
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
    }
}
