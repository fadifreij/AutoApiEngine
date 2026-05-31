namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class DdlStatementResult
    {
        public int Index { get; set; }
        public string Sql { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int RowsAffected { get; set; }
        public long DurationMs { get; set; }
    }
}