using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;

namespace AutoApiEngine.ServiceAbstraction
{
    public class BackupHistoryItem
    {
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class DatabaseStatsResult
    {
        public int TablesCount { get; set; }
        public int FunctionsCount { get; set; }
        public int StoredProceduresCount { get; set; }
        public long DatabaseSizeBytes { get; set; }
        public List<BackupHistoryItem> BackupHistory { get; set; } = new();
    }

    public interface IDatabaseManagementService
    {
        Task<string> CreateDatabaseAsync(string databaseName, DatabaseEngine engine, CancellationToken cancellationToken = default);

        Task<DatabaseStatsResult> GetDatabaseStatsAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default);

        Task BackupAsync(
            DatabaseEngine engine,
            string backupPath,
            string connectionString,
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