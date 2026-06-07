namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Describes a single column returned by a query result set.
    /// </summary>
    public class QueryResultColumnDto
    {
        /// <summary>Column name as returned by the database.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Data type name as returned by the database provider (e.g. int, nvarchar, datetime).</summary>
        public string DataType { get; set; } = string.Empty;
    }
}
