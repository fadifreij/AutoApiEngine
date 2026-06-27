using AutoApiEngine.Domain.Enums;

namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class WorkspaceStatsDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? DatabaseName { get; set; }
        public string? ServerHost { get; set; }
        public DatabaseEngine DatabaseEngine { get; set; }
        public int TablesCount { get; set; }
        public int ViewsCount { get; set; }
        public int FunctionsCount { get; set; }
        public int StoredProceduresCount { get; set; }
        public long? DatabaseSizeBytes { get; set; }
        public DateTime? LastSyncAt { get; set; }
        public bool IsActive { get; set; }
        public List<BackupHistoryItem> BackupHistory { get; set; } = new();
    }
}