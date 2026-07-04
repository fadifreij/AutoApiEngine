using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// MySQL implementation of <see cref="IForeignKeyService"/>.
    /// Uses information_schema.KEY_COLUMN_USAGE and REFERENTIAL_CONSTRAINTS.
    /// </summary>
    public class MySqlForeignKeyService : IForeignKeyService
    {
        private readonly ILogger<MySqlForeignKeyService> _logger;
        private readonly string _connectionString;

        public MySqlForeignKeyService(ILogger<MySqlForeignKeyService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("MySqlConnection")
                ?? "Server=localhost;Port=3307;Uid=root;Pwd=root;";
        }

        public async Task<ForeignKeyInfoDto> GetForeignKeysAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string tableName,
            CancellationToken cancellationToken = default)
        {
            var connStr = BuildConnectionString(connectionString, databaseName);
            await using var connection = new MySqlConnection(connStr);
            await connection.OpenAsync(cancellationToken);

            var result = new ForeignKeyInfoDto
            {
                TableName = tableName,
                TableSchema = databaseName
            };

            // ── Outgoing FKs (this table → others) ──
            const string outgoingSql = @"
                SELECT
                    kcu.CONSTRAINT_NAME         AS FK_Name,
                    kcu.COLUMN_NAME             AS ColumnName,
                    kcu.REFERENCED_TABLE_SCHEMA AS RefSchema,
                    kcu.REFERENCED_TABLE_NAME   AS RefTable,
                    kcu.REFERENCED_COLUMN_NAME  AS RefColumn
                FROM information_schema.KEY_COLUMN_USAGE kcu
                WHERE kcu.TABLE_SCHEMA = @db
                  AND kcu.TABLE_NAME = @tableName
                  AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
                ORDER BY kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION";

            await using (var cmd = new MySqlCommand(outgoingSql, connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                cmd.Parameters.AddWithValue("@tableName", tableName);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.ForeignKeys.Add(new ForeignKeyDetailDto
                    {
                        FkName = reader.GetString(0),
                        Column = reader.GetString(1),
                        ReferencedSchema = reader.IsDBNull(2) ? databaseName : reader.GetString(2),
                        ReferencedTable = reader.GetString(3),
                        ReferencedColumn = reader.GetString(4)
                    });
                }
            }

            // ── Incoming FKs (other tables → this table) ──
            const string incomingSql = @"
                SELECT
                    kcu.CONSTRAINT_NAME AS FK_Name,
                    kcu.TABLE_SCHEMA    AS TableSchema,
                    kcu.TABLE_NAME      AS TableName,
                    kcu.COLUMN_NAME     AS ColumnName,
                    kcu.REFERENCED_COLUMN_NAME AS RefColumn
                FROM information_schema.KEY_COLUMN_USAGE kcu
                WHERE kcu.REFERENCED_TABLE_SCHEMA = @db
                  AND kcu.REFERENCED_TABLE_NAME = @tableName
                ORDER BY kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION";

            await using (var cmd2 = new MySqlCommand(incomingSql, connection))
            {
                cmd2.Parameters.AddWithValue("@db", databaseName);
                cmd2.Parameters.AddWithValue("@tableName", tableName);
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

        private static string BuildConnectionString(string? connectionString, string databaseName)
        {
            var baseConn = string.IsNullOrWhiteSpace(connectionString)
                ? "Server=localhost;Port=3307;Uid=root;Pwd=root;"
                : connectionString;
            return $"{baseConn.TrimEnd(';')};Database={databaseName}";
        }
    }
}
