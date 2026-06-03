using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.ServiceAbstraction
{
    /// <summary>
    /// Provides schema exploration for a database — enumerating tables, views,
    /// stored procedures, functions, and their column counts.
    /// </summary>
    public interface ISchemaExplorerService
    {
        /// <summary>
        /// Retrieves all schema objects (tables, views, SPs, functions) for the given database,
        /// optionally filtered by a search term.
        /// </summary>
        Task<SchemaExplorerResponse> ExploreAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string? searchFilter = null,
            CancellationToken cancellationToken = default);
    }
}
