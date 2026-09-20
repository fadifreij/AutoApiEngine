namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Full metadata for a database object, combining column info and FK info.
    /// Used by the frontend to build the column selector / include builder UI.
    /// </summary>
    public class DynamicApiObjectMetadataDto
    {
        public string ObjectName { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty; // "Table", "View", "StoredProcedure", "Function"
        public string Schema { get; set; } = "dbo";
        public List<ColumnMetadataDto> Columns { get; set; } = new();
        public List<string> PrimaryKeyColumns { get; set; } = new();
        public List<ForeignKeyDetailDto> ForeignKeys { get; set; } = new();
        public List<ReferencedByDetailDto> ReferencedBy { get; set; } = new();

        /// <summary>
        /// HTTP verb the object is exposed as: "GET" | "POST" for stored procedures,
        /// "GET" for views/functions, null for tables.
        /// </summary>
        public string? Verb { get; set; }

        /// <summary>
        /// Routine parameters for stored procedures and functions (IN/OUT/INOUT).
        /// Empty for tables.
        /// </summary>
        public List<RoutineParameterDto> Parameters { get; set; } = new();
    }

    /// <summary>
    /// Column metadata for the UI.
    /// </summary>
    public class ColumnMetadataDto
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
    }
}
