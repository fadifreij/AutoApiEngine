using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// SQL Server implementation of <see cref="IForeignKeyService"/>.
    /// Uses sys.foreign_keys and sys.foreign_key_columns system views.
    /// </summary>
    public class SqlForeignKeyService : IForeignKeyService
    {
        private readonly ILogger<SqlForeignKeyService> _logger;
        private readonly string _connectionString;

        public SqlForeignKeyService(ILogger<SqlForeignKeyService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("SqlServerConnection")
                ?? throw new InvalidOperationException("SqlServerConnection is not configured.");
        }

        public async Task<ForeignKeyInfoDto> GetForeignKeysAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string tableName,
            CancellationToken cancellationToken = default)
        {
            var connStr = BuildConnectionString(connectionString, databaseName);
            await using var connection = new SqlConnection(connStr);
            await connection.OpenAsync(cancellationToken);

            // Resolve schema
            var schema = await ResolveSchemaAsync(connection, tableName, cancellationToken);

            var result = new ForeignKeyInfoDto
            {
                TableName = tableName,
                TableSchema = schema
            };

            // ── Outgoing FKs (this table → others) ──
            const string outgoingSql = @"
                SELECT
                    fk.name                                          AS FK_Name,
                    COL_NAME(fkc.parent_object_id, fkc.parent_column_id)          AS ColumnName,
                    OBJECT_SCHEMA_NAME(fkc.referenced_object_id)                  AS RefSchema,
                    OBJECT_NAME(fkc.referenced_object_id)                         AS RefTable,
                    COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id)  AS RefColumn
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc
                    ON fkc.constraint_object_id = fk.object_id
                WHERE fk.parent_object_id = OBJECT_ID(@qualifiedName)
                ORDER BY fk.name, fkc.constraint_column_id";

            await using (var cmd = new SqlCommand(outgoingSql, connection))
            {
                cmd.Parameters.AddWithValue("@qualifiedName", $"[{schema}].[{tableName}]");
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.ForeignKeys.Add(new ForeignKeyDetailDto
                    {
                        FkName = reader.GetString(0),
                        Column = reader.GetString(1),
                        ReferencedSchema = reader.GetString(2),
                        ReferencedTable = reader.GetString(3),
                        ReferencedColumn = reader.GetString(4)
                    });
                }
            }

            // ── Incoming FKs (other tables → this table) ──
            const string incomingSql = @"
                SELECT
                    fk.name                                          AS FK_Name,
                    OBJECT_SCHEMA_NAME(fkc.parent_object_id)                     AS TableSchema,
                    OBJECT_NAME(fkc.parent_object_id)                            AS TableName,
                    COL_NAME(fkc.parent_object_id, fkc.parent_column_id)         AS ColumnName,
                    COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS RefColumn
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc
                    ON fkc.constraint_object_id = fk.object_id
                WHERE fkc.referenced_object_id = OBJECT_ID(@qualifiedName)
                ORDER BY fk.name, fkc.constraint_column_id";

            await using (var cmd2 = new SqlCommand(incomingSql, connection))
            {
                cmd2.Parameters.AddWithValue("@qualifiedName", $"[{schema}].[{tableName}]");
                await using var reader = await cmd2.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.ReferencedBy.Add(new ReferencedByDetailDto
                    {
                        FkName = reader.GetString(0),
                        TableSchema = reader.GetString(1),
                        Table = reader.GetString(2),
                        Column = reader.GetString(3),
                        ReferencedColumn = reader.GetString(4)
                    });
                }
            }

            return result;
        }

        private static async Task<string> ResolveSchemaAsync(
            SqlConnection connection,
            string tableName,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT TOP 1 TABLE_SCHEMA
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = @name AND TABLE_TYPE = 'BASE TABLE'";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@name", tableName);
            return (await cmd.ExecuteScalarAsync(cancellationToken) as string) ?? "dbo";
        }

        private static string BuildConnectionString(string? connectionString, string databaseName)
        {
            var baseConn = string.IsNullOrWhiteSpace(connectionString)
                ? "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True;"
                : connectionString;

            var csb = new SqlConnectionStringBuilder(baseConn)
            {
                InitialCatalog = databaseName
            };
            return csb.ConnectionString;
        }
    }
}
