namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// A table and its columns, used to give the AI assistant the full structure
    /// of the current database (table names, column names, types, nullability, keys).
    /// </summary>
    public class TableSchemaDto
    {
        public string TableName { get; set; } = string.Empty;
        public List<TableColumnDto> Columns { get; set; } = new();
    }

    /// <summary>A single column belonging to a <see cref="TableSchemaDto"/>.</summary>
    public class TableColumnDto
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
    }

    /// <summary>
    /// The definition (body) of a programmable database object — a view, stored
    /// procedure, or function — used to give the AI assistant full context.
    /// </summary>
    public class RoutineDefinitionDto
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>"View", "StoredProcedure", or "Function".</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>The SQL definition / body of the object.</summary>
        public string Definition { get; set; } = string.Empty;
    }
}
