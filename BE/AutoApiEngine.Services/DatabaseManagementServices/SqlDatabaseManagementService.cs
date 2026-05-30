using AutoApiEngine.ServiceAbstraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using Microsoft.Data.SqlClient;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    public class SqlDatabaseManagementService : IDatabaseManagementService
    {
        private static readonly Regex PercentRegex = new(@"(\d+)\spercent", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly ILogger<SqlDatabaseManagementService> _logger;
        private readonly string _appConnectionString;

        public SqlDatabaseManagementService(ILogger<SqlDatabaseManagementService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _appConnectionString = configuration.GetConnectionString("SqlServerConnection")
                ?? throw new InvalidOperationException("SqlServerConnection is not configured.");
        }

        public async Task<string> CreateDatabaseAsync(string databaseName, DatabaseEngine engine, CancellationToken cancellationToken = default)
        {
            var csb = new SqlConnectionStringBuilder(_appConnectionString)
            {
                InitialCatalog = "master"
            };

            await using var connection = new SqlConnection(csb.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $@"
                IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = @name)
                BEGIN
                    CREATE DATABASE [{databaseName}]
                END";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@name", databaseName);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            return databaseName;
        }

        public async Task<DatabaseStatsResult> GetDatabaseStatsAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default)
        {
            var result = new DatabaseStatsResult();

            var targetConn = string.IsNullOrWhiteSpace(connectionString) ? _appConnectionString : connectionString;
            var csb = new SqlConnectionStringBuilder(targetConn)
            {
                InitialCatalog = databaseName
            };

            await using var connection = new SqlConnection(csb.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'", connection))
            {
                result.TablesCount = (int)await cmd.ExecuteScalarAsync(cancellationToken);
            }

            await using (var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.VIEWS", connection))
            {
                result.ViewsCount = (int)await cmd.ExecuteScalarAsync(cancellationToken);
            }

            await using (var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'FUNCTION'", connection))
            {
                result.FunctionsCount = (int)await cmd.ExecuteScalarAsync(cancellationToken);
            }

            await using (var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE'", connection))
            {
                result.StoredProceduresCount = (int)await cmd.ExecuteScalarAsync(cancellationToken);
            }

            await using (var cmd = new SqlCommand(
                "SELECT ISNULL(SUM(CAST(size AS BIGINT) * 8 * 1024), 0) FROM sys.database_files WHERE type = 0", connection))
            {
                result.DatabaseSizeBytes = (long)await cmd.ExecuteScalarAsync(cancellationToken);
            }


            return result;
        }


        public async Task BackupAsync(
            DatabaseEngine engine,
            string backupPath,
            string databaseName,
            IProgress<DatabaseProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_appConnectionString);

            connection.InfoMessage += (_, e) =>
            {
                foreach (SqlError error in e.Errors)
                {
                    var match = PercentRegex.Match(error.Message);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                    {
                        progress?.Report(new DatabaseProgress
                        {
                            Percentage = percent,
                            Message = error.Message
                        });
                    }
                }
            };

            await connection.OpenAsync(cancellationToken);

            var commandText = $@"
                BACKUP DATABASE [{databaseName}]
                TO DISK = @BackupPath
                WITH STATS = 5";

            await using var command = new SqlCommand(commandText, connection);
            command.Parameters.AddWithValue("@BackupPath", backupPath);

            await command.ExecuteNonQueryAsync(cancellationToken);

            progress?.Report(new DatabaseProgress { Percentage = 100, Message = "Backup completed" });
        }

        public async Task RestoreAsync(
            DatabaseEngine engine,
            string backupPath,
            DatabaseOptions databaseOptions,
            IProgress<DatabaseProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Must connect to master database to perform restore operations, not the target database
            var csb = new SqlConnectionStringBuilder(databaseOptions.ConnectionString)
            {
                InitialCatalog = "master"
            };

            await using var connection = new SqlConnection(csb.ConnectionString);
            
            // Enable FireInfoMessageEventOnUserErrors to receive progress messages during long operations
            connection.FireInfoMessageEventOnUserErrors = true;

            connection.InfoMessage += (_, e) =>
            {
                foreach (SqlError error in e.Errors)
                {
                    var match = PercentRegex.Match(error.Message);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                    {
                        progress?.Report(new DatabaseProgress
                        {
                            Percentage = percent,
                            Message = error.Message
                        });
                    }
                }
            };

            await connection.OpenAsync(cancellationToken);

            // Kill all existing connections to the database before restore
            await ExecuteAsync(connection, $@"
                IF EXISTS (SELECT name FROM sys.databases WHERE name = '{databaseOptions.DatabaseName}')
                BEGIN
                    ALTER DATABASE [{databaseOptions.DatabaseName}]
                    SET SINGLE_USER WITH ROLLBACK IMMEDIATE
                END", cancellationToken);

            var restoreCommand = new SqlCommand($@"
                RESTORE DATABASE [{databaseOptions.DatabaseName}]
                FROM DISK = @BackupPath
                WITH REPLACE, STATS = 5", connection);

            restoreCommand.Parameters.AddWithValue("@BackupPath", backupPath);
            
            // Set a longer timeout for restore operations (30 minutes)
            restoreCommand.CommandTimeout = 1800;

            await restoreCommand.ExecuteNonQueryAsync(cancellationToken);

            await ExecuteAsync(connection, $@"
                ALTER DATABASE [{databaseOptions.DatabaseName}]
                SET MULTI_USER", cancellationToken);

            progress?.Report(new DatabaseProgress { Percentage = 100, Message = "Restore completed" });
        }

        private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken ct)
        {
            await using var cmd = new SqlCommand(sql, connection);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
