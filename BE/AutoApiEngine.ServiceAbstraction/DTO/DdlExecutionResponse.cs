namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class DdlExecutionResponse
    {
        public List<DdlStatementResult> Statements { get; set; } = new();
        public bool OverallSuccess => Statements.All(s => s.Success);
        public int TotalStatements => Statements.Count;
        public int Succeeded => Statements.Count(s => s.Success);
        public int Failed => Statements.Count(s => !s.Success);
    }
}