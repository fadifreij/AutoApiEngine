using AutoApiEngine.ServiceAbstraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using MySql.Data.MySqlClient;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    public class MySqlDatabaseManagementService : IDatabaseManagementService
    {
        private readonly ILogger<MySqlDatabaseManagementService> _logger;
        private readonly string _mySqlConnection;

        public MySqlDatabaseManagementService(ILogger<MySqlDatabaseManagementService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _mySqlConnection = configuration.GetConnectionString("MySqlConnection")
                ?? "Server=localhost;Port=3307;Uid=root;Pwd=root;";
        }

        public async Task<string> CreateDatabaseAsync(string databaseName, DatabaseEngine engine, CancellationToken cancellationToken = default)
        {
            await using var connection = new MySqlConnection(_mySqlConnection);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = new MySqlCommand(
                $"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
                connection);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            return databaseName;
        }

        public async Task<DatabaseStatsResult> GetDatabaseStatsAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default)
        {
            var result = new DatabaseStatsResult();
            var connStr = string.IsNullOrWhiteSpace(connectionString)
                ? $"{_mySqlConnection};Database={databaseName}"
                : $"{connectionString};Database={databaseName}";

            await using var connection = new MySqlConnection(connStr);
            await connection.OpenAsync(cancellationToken);

            await using (var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @db AND table_type = 'BASE TABLE'",
                connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                result.TablesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            await using (var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = @db AND routine_type = 'FUNCTION'",
                connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                result.FunctionsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            await using (var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = @db AND routine_type = 'PROCEDURE'",
                connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                result.StoredProceduresCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            await using (var cmd = new MySqlCommand(
                "SELECT IFNULL(SUM(data_length + index_length), 0) FROM information_schema.tables WHERE table_schema = @db",
                connection))
            {
                cmd.Parameters.AddWithValue("@db", databaseName);
                var val = await cmd.ExecuteScalarAsync(cancellationToken);
                result.DatabaseSizeBytes = val is DBNull or null ? 0L : Convert.ToInt64(val);
            }

            return result;
        }

        public async Task BackupAsync(
            string backupPath,
            DatabaseOptions databaseOptions,
            IProgress<DatabaseProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new MySqlConnection(databaseOptions.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = new MySqlCommand($"BACKUP DATABASE `{databaseOptions.DatabaseName}` TO DISK = @path", connection);
            cmd.Parameters.AddWithValue("@path", backupPath);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            progress?.Report(new DatabaseProgress { Percentage = 100, Message = "Backup completed" });
        }

        public async Task RestoreAsync(
            string backupPath,
            DatabaseOptions databaseOptions,
            IProgress<DatabaseProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new MySqlConnection(databaseOptions.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = new MySqlCommand($"RESTORE DATABASE `{databaseOptions.DatabaseName}` FROM DISK = @path", connection);
            cmd.Parameters.AddWithValue("@path", backupPath);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            progress?.Report(new DatabaseProgress { Percentage = 100, Message = "Restore completed" });
        }
    }
}