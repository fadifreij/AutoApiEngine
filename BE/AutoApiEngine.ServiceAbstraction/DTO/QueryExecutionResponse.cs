namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Response returned after executing a SQL query.
    /// Handles both SELECT (result-set) and DML (non-result-set) queries.
    /// </summary>
    public class QueryExecutionResponse
    {
        /// <summary>Whether the query execution succeeded.</summary>
        public bool Success { get; set; }

        /// <summary>Error message if <c>Success</c> is false.</summary>
        public string? Error { get; set; }

        /// <summary>True if the query produced a result set (e.g. SELECT, EXEC).</summary>
        public bool IsSelectQuery { get; set; }

        /// <summary>Column metadata. Populated when <c>IsSelectQuery</c> is true.</summary>
        public List<QueryResultColumnDto> Columns { get; set; } = new();

        /// <summary>Data rows. Populated when <c>IsSelectQuery</c> is true.</summary>
        public List<QueryResultRowDto> Rows { get; set; } = new();

        /// <summary>Number of rows affected (for DML statements like INSERT/UPDATE/DELETE).</summary>
        public int RowsAffected { get; set; }

        /// <summary>Total execution time in milliseconds.</summary>
        public long DurationMs { get; set; }

        /// <summary>Total number of rows returned (for result-set queries).</summary>
        public int TotalRows => Rows.Count;
    }
}
