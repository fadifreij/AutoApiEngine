namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Represents a single schema object (table, view, stored procedure, or function).
    /// </summary>
    public class SchemaObjectDto
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "Table", "View", "StoredProcedure", "Function"
        public int? ColumnCount { get; set; } // Only populated for tables
    }
}
