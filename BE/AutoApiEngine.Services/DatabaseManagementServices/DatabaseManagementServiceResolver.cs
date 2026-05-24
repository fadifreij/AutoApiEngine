using AutoApiEngine.ServiceAbstraction;
using Microsoft.Extensions.Logging;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.Domain.Entities;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Delegates database management operations to concrete implementations based on engine.
    /// Registered as the primary <see cref="IDatabaseManagementService"/> so callers can pass the engine.
    /// </summary>
    public class DatabaseManagementServiceResolver : IDatabaseManagementService
    {
        private readonly ILogger<DatabaseManagementServiceResolver> _logger;
        private readonly SqlDatabaseManagementService _sql;
        private readonly MySqlDatabaseManagementService _mysql;

        public DatabaseManagementServiceResolver(
            ILogger<DatabaseManagementServiceResolver> logger,
            SqlDatabaseManagementService sql,
            MySqlDatabaseManagementService mysql)
        {
            _logger = logger;
            _sql = sql;
            _mysql = mysql;
        }

        public Task<string> CreateDatabaseAsync(string databaseName, DatabaseEngine engine, CancellationToken cancellationToken = default)
        {
            return engine switch
            {
                DatabaseEngine.MySql => _mysql.CreateDatabaseAsync(databaseName, engine, cancellationToken),
                _ => _sql.CreateDatabaseAsync(databaseName, engine, cancellationToken)
            };
        }

        public Task<DatabaseStatsResult> GetDatabaseStatsAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default)
        {
            return engine switch
            {
                DatabaseEngine.MySql => _mysql.GetDatabaseStatsAsync(databaseName, engine, connectionString, cancellationToken),
                _ => _sql.GetDatabaseStatsAsync(databaseName, engine, connectionString, cancellationToken)
            };
        }

        public Task BackupAsync(DatabaseEngine engine, string backupPath, string databaseName, IProgress<DatabaseProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            return engine switch
            {
                DatabaseEngine.MySql => _mysql.BackupAsync(engine, backupPath, databaseName, progress, cancellationToken),
                _ => _sql.BackupAsync(engine, backupPath, databaseName, progress, cancellationToken)
            };
        }

        public Task RestoreAsync(DatabaseEngine engine, string backupPath, DatabaseOptions databaseOptions, IProgress<DatabaseProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            return engine switch
            {
                DatabaseEngine.MySql => _mysql.RestoreAsync(engine, backupPath, databaseOptions, progress, cancellationToken),
                _ => _sql.RestoreAsync(engine, backupPath, databaseOptions, progress, cancellationToken)
            };
        }
    }
}
