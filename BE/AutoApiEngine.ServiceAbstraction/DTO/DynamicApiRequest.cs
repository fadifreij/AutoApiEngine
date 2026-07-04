namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Query parameters parsed from the dynamic API request URL.
    /// </summary>
    public class DynamicApiQueryRequest
    {
        /// <summary>
        /// Column names to return. Supports dot-notation for related tables: "department.name".
        /// Pass multiple times for multiple columns: ?select=Id&amp;select=Name
        /// Or use comma-separated (legacy): ?select=Id,Name
        /// If null or empty, all columns are returned.
        /// </summary>
        public List<string>? Select { get; set; }

        /// <summary>
        /// Comma-separated related table includes with optional column lists.
        /// Format: "orders(id,total),department(name)"
        /// </summary>
        public string? Include { get; set; }

        /// <summary>
        /// Filter expressions in format "column:operator:value".
        /// Multiple values are AND-ed by default.
        /// Prefix with "or:" for OR logic: "or:name:contains:john"
        /// </summary>
        public List<string>? Filter { get; set; }

        /// <summary>
        /// Sort expressions in format "column:direction".
        /// Pass multiple times for multiple sorts: ?sort=lastName:asc&amp;sort=firstName:asc
        /// Or use comma-separated (legacy): ?sort=lastName:asc,firstName:asc
        /// </summary>
        public List<string>? Sort { get; set; }

        /// <summary>
        /// Page number (1-based). Default: 1.
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// Page size. Default: 100, Max: 1000.
        /// Set to 0 to return all records (no paging).
        /// </summary>
        public int PageSize { get; set; } = 100;
    }

    /// <summary>
    /// Request body for creating a new record.
    /// A dictionary of column name → value pairs.
    /// </summary>
    public class DynamicApiCreateRequest : Dictionary<string, object?>
    {
        public DynamicApiCreateRequest() { }
        public DynamicApiCreateRequest(IDictionary<string, object?> dictionary) : base(dictionary) { }
    }

    /// <summary>
    /// Request body for updating a record.
    /// A dictionary of column name → value pairs (partial update / PATCH semantics).
    /// </summary>
    public class DynamicApiUpdateRequest : Dictionary<string, object?>
    {
        public DynamicApiUpdateRequest() { }
        public DynamicApiUpdateRequest(IDictionary<string, object?> dictionary) : base(dictionary) { }
    }
}
