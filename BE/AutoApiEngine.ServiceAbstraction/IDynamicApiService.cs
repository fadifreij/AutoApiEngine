using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.ServiceAbstraction
{
    /// <summary>
    /// Generic dynamic API service that exposes any database table, view,
    /// stored procedure, or function as RESTful endpoints without requiring
    /// any pre-defined controllers, DTOs, or EF Core entities.
    /// </summary>
    public interface IDynamicApiService
    {
        /// <summary>
        /// Lists records with optional filtering, sorting, paging, and related-table expansion.
        /// </summary>
        Task<DynamicApiListResponse> GetListAsync(
            string workspaceId,
            string objectName,
            DynamicApiQueryRequest query,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a single record by its primary key.
        /// For single-column PKs, pass the value as a string.
        /// </summary>
        Task<DynamicApiSingleResponse> GetByIdAsync(
            string workspaceId,
            string objectName,
            string id,
            DynamicApiQueryRequest? query = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a single record by a composite primary key.
        /// </summary>
        Task<DynamicApiSingleResponse> GetByCompositeKeyAsync(
            string workspaceId,
            string objectName,
            Dictionary<string, string> pkValues,
            DynamicApiQueryRequest? query = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new record in the specified table.
        /// </summary>
        Task<DynamicApiActionResponse> CreateAsync(
            string workspaceId,
            string objectName,
            Dictionary<string, object?> data,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates a record by its primary key (partial update / PATCH semantics).
        /// </summary>
        Task<DynamicApiActionResponse> UpdateAsync(
            string workspaceId,
            string objectName,
            string id,
            Dictionary<string, object?> data,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a record by its primary key.
        /// </summary>
        Task<DynamicApiActionResponse> DeleteAsync(
            string workspaceId,
            string objectName,
            string id,
            CancellationToken cancellationToken = default);
    }
}
