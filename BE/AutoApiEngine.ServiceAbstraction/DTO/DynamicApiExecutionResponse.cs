namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Response returned when executing a stored procedure or function.
    /// Carries all result sets, read-back output parameter values, and the affected-row count.
    /// </summary>
    public class DynamicApiExecutionResponse
    {
        public List<Dictionary<string, object?>>? ResultSets { get; set; }
        public Dictionary<string, object?>? OutputParams { get; set; }
        public int RowsAffected { get; set; }
    }
}