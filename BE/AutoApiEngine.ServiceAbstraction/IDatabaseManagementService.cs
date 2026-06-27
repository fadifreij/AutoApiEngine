using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction.DTO;
using System.Data.Common;

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
        public int ViewsCount { get; set; }
        public int FunctionsCount { get; set; }
        public int StoredProceduresCount { get; set; }
        public long DatabaseSizeBytes { get; set; }
        public List<BackupHistoryItem> BackupHistory { get; set; } = new();
    }

    public interface IDatabaseManagementService
    {
        Task<string> CreateDatabaseAsync(string databaseName, DatabaseEngine engine, CancellationToken cancellationToken = default);

        Task<DatabaseStatsResult> GetDatabaseStatsAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default);

        Task<TestConnectionResult> TestConnectionAsync(string serverHost, string? userName, string? password, string? databaseName, DatabaseEngine engine, CancellationToken cancellationToken = default);

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

        // ═══════════════════════════════════════════════════════════════
        // Shared default-implementation helpers (reduce code duplication)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Shared execution engine for database stats queries.
        /// Implementations call this with engine-specific SQL strings and optional parameters.
        /// Handles the 5-query pattern (tables, views, functions, procedures, size) and
        /// automatically populates backup history.
        /// </summary>
        protected static async Task<DatabaseStatsResult> ExecuteStatsQueriesAsync(
            DbConnection connection,
            string databaseName,
            string tableCountSql, Dictionary<string, object>? tableCountParams,
            string viewCountSql, Dictionary<string, object>? viewCountParams,
            string functionCountSql, Dictionary<string, object>? functionCountParams,
            string procedureCountSql, Dictionary<string, object>? procedureCountParams,
            string sizeBytesSql, Dictionary<string, object>? sizeBytesParams,
            CancellationToken cancellationToken)
        {
            var result = new DatabaseStatsResult();

            await using (var cmd = BuildCommand(connection, tableCountSql, tableCountParams))
                result.TablesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = BuildCommand(connection, viewCountSql, viewCountParams))
                result.ViewsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = BuildCommand(connection, functionCountSql, functionCountParams))
                result.FunctionsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = BuildCommand(connection, procedureCountSql, procedureCountParams))
                result.StoredProceduresCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = BuildCommand(connection, sizeBytesSql, sizeBytesParams))
            {
                var val = await cmd.ExecuteScalarAsync(cancellationToken);
                result.DatabaseSizeBytes = val is DBNull or null ? 0L : Convert.ToInt64(val);
            }

            PopulateBackupHistory(result, databaseName);
            return result;
        }

        /// <summary>
        /// Populates backup history for the given database by scanning the local backups directory.
        /// Returns the 5 most recent backups, sorted newest-first.
        /// </summary>
        protected static void PopulateBackupHistory(DatabaseStatsResult result, string databaseName)
        {
            try
            {
                var backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
                if (!Directory.Exists(backupDir))
                    return;

                var matchingFiles = Directory.GetFiles(backupDir, $"{databaseName}_*", SearchOption.AllDirectories);

                foreach (var filePath in matchingFiles)
                {
                    var fi = new FileInfo(filePath);
                    result.BackupHistory.Add(new BackupHistoryItem
                    {
                        FileName = fi.Name,
                        SizeBytes = fi.Length,
                        CreatedAt = fi.LastWriteTimeUtc
                    });
                }

                result.BackupHistory = result.BackupHistory
                    .OrderByDescending(h => h.CreatedAt)
                    .Take(5)
                    .ToList();
            }
            catch
            {
                // Silently handle directory/file access issues
            }
        }

        private static DbCommand BuildCommand(DbConnection connection, string sql, Dictionary<string, object>? parameters)
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