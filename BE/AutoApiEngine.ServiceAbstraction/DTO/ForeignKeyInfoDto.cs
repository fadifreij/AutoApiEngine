namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Full foreign key information for a table, including both outgoing FKs
    /// (columns that reference other tables) and incoming references
    /// (other tables that reference this table).
    /// </summary>
    public class ForeignKeyInfoDto
    {
        public string TableName { get; set; } = string.Empty;
        public string TableSchema { get; set; } = "dbo";
        public List<ForeignKeyDetailDto> ForeignKeys { get; set; } = new();
        public List<ReferencedByDetailDto> ReferencedBy { get; set; } = new();
    }

    /// <summary>
    /// A foreign key column on this table that references another table.
    /// </summary>
    public class ForeignKeyDetailDto
    {
        public string FkName { get; set; } = string.Empty;
        public string Column { get; set; } = string.Empty;
        public string ReferencedTable { get; set; } = string.Empty;
        public string ReferencedColumn { get; set; } = string.Empty;
        public string ReferencedSchema { get; set; } = "dbo";
    }

    /// <summary>
    /// A table that has a foreign key referencing this table.
    /// </summary>
    public class ReferencedByDetailDto
    {
        public string FkName { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public string Column { get; set; } = string.Empty;
        public string ReferencedColumn { get; set; } = string.Empty;
        public string TableSchema { get; set; } = "dbo";
    }
}
