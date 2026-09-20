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

        /// <summary>
        /// Retrieves every base table in the database together with its full column list
        /// (name, type, nullability, primary-key flag). Used to give the AI assistant
        /// complete structural context for the current database.
        /// </summary>
        Task<List<TableSchemaDto>> GetTableColumnsAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the definitions (SQL bodies) of all views, stored procedures, and
        /// functions in the database, so the AI assistant understands what they do.
        /// </summary>
        Task<List<RoutineDefinitionDto>> GetRoutineDefinitionsAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the full CREATE DDL for a single database object (table, view,
        /// stored procedure, or function). Used by Query Studio's schema context menu
        /// for "Copy to clipboard" / "Copy to editor" actions.
        /// </summary>
        Task<string> GetObjectDdlAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string objectName,
            string objectType,  // "Table", "View", "StoredProcedure", "Function"
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the declared parameters of a stored procedure or function in
        /// declaration order. Views return an empty list (no parameter catalog rows).
        /// SQL Server: <c>sys.parameters</c> (+ <c>sys.types</c> for the type name).
        /// MySQL: <c>information_schema.PARAMETERS</c>.
        /// </summary>
        Task<List<RoutineParameterDto>> GetRoutineParametersAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string schema,
            string objectName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the SQL definition (body) of a single stored procedure. Used for
        /// HTTP-verb classification; returns an empty string when unavailable.
        /// </summary>
        Task<string> GetRoutineDefinitionAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string schema,
            string objectName,
            CancellationToken cancellationToken = default);
    }
}
