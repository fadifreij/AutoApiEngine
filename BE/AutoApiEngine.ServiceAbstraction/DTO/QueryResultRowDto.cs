namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// A single row of query results. Each entry corresponds to a column in <see cref="QueryExecutionResponse.Columns"/>.
    /// Null values are serialized as null (not the string "null").
    /// </summary>
    public class QueryResultRowDto
    {
        /// <summary>Column values in the same order as <c>Columns</c>.</summary>
        public List<string?> Values { get; set; } = new();
    }
}
