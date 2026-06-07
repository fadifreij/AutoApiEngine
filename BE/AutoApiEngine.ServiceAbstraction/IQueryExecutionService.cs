using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.ServiceAbstraction
{
    /// <summary>
    /// Service that executes arbitrary SQL queries against a workspace's database.
    /// Supports both SELECT (result-set) and DML (non-result-set) queries.
    /// </summary>
    public interface IQueryExecutionService
    {
        /// <summary>
        /// Executes a SQL query and returns the results.
        /// For SELECT queries, returns column metadata and data rows.
        /// For INSERT/UPDATE/DELETE, returns rows affected.
        /// </summary>
        /// <param name="request">The query execution request containing workspace ID and SQL.</param>
        /// <param name="cancellationToken">Token to cancel the query execution mid-flight.</param>
        /// <returns>Query execution response with appropriate data based on query type.</returns>
        Task<QueryExecutionResponse> ExecuteAsync(QueryExecutionRequest request, CancellationToken cancellationToken = default);
    }
}
