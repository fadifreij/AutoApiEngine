using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;

namespace AutoApiEngine.ServiceAbstraction
{
    public class DatabaseStatsResult
    {
        public int TablesCount { get; set; }
        public int FunctionsCount { get; set; }
        public int StoredProceduresCount { get; set; }
        public long DatabaseSizeBytes { get; set; }
    }

    public interface IDatabaseManagementService
    {
        Task<string> CreateDatabaseAsync(string databaseName, DatabaseEngine engine, CancellationToken cancellationToken = default);

        Task<DatabaseStatsResult> GetDatabaseStatsAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default);

        Task BackupAsync(
            DatabaseEngine engine,
            string backupPath,
            DatabaseOptions databaseOptions,
            IProgress<DatabaseProgress>? progress = null,
            CancellationToken cancellationToken = default);

        Task RestoreAsync(
            DatabaseEngine engine,
            string backupPath,
            DatabaseOptions databaseOptions,
            IProgress<DatabaseProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}