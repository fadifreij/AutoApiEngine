using AutoApiEngine.ServiceAbstraction;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using AutoApiEngine.Domain.Entities;
using Microsoft.Data.SqlClient;
namespace AutoApiEngine.Services.DatabaseManagementServices
{
    public class SqlDatabaseManagementService : IDatabaseManagementService
    {
        private static readonly Regex PercentRegex = new(@"(\d+)\spercent", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private readonly ILogger<SqlDatabaseManagementService> _logger;

        public SqlDatabaseManagementService(
         ILogger<SqlDatabaseManagementService> logger)
        {
           
            _logger = logger;
        }
        public async Task BackupAsync(
          string backupPath,
          DatabaseOptions databaseOptions,
          IProgress<DatabaseProgress>? progress = null,
          CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(databaseOptions.ConnectionString);

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
                BACKUP DATABASE [{databaseOptions.DatabaseName}]
                TO DISK = @BackupPath
                WITH STATS = 5";

            await using var command = new SqlCommand(commandText, connection);
            command.Parameters.AddWithValue("@BackupPath", backupPath);

            await command.ExecuteNonQueryAsync(cancellationToken);

            progress?.Report(new DatabaseProgress { Percentage = 100, Message = "Backup completed" });
        }
        public async Task RestoreAsync(
          string backupPath,
          DatabaseOptions databaseOptions,
          IProgress<DatabaseProgress>? progress = null,
          CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(databaseOptions.ConnectionString);

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

            await ExecuteAsync(connection, $@"
                ALTER DATABASE [{databaseOptions.DatabaseName}]
                SET SINGLE_USER WITH ROLLBACK IMMEDIATE", cancellationToken);

            var restoreCommand = new SqlCommand($@"
                RESTORE DATABASE [{databaseOptions.DatabaseName}]
                FROM DISK = @BackupPath
                WITH REPLACE, STATS = 5", connection);

            restoreCommand.Parameters.AddWithValue("@BackupPath", backupPath);

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
