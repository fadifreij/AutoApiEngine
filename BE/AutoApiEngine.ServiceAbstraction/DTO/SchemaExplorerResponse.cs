namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Response returned by the schema explorer endpoint.
    /// Contains summary counts and detailed object lists.
    /// </summary>
    public class SchemaExplorerResponse
    {
        public int TablesCount { get; set; }
        public int ViewsCount { get; set; }
        public int FunctionsCount { get; set; }
        public int StoredProceduresCount { get; set; }
        public long DatabaseSizeBytes { get; set; }
        public List<SchemaObjectDto> Objects { get; set; } = new();
    }
}
