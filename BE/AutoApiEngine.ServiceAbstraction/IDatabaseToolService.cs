using AutoApiEngine.Domain.Enums;

namespace AutoApiEngine.ServiceAbstraction
{
    /// <summary>
    /// Provides MCP-style database tools that the AI assistant can invoke dynamically.
    /// Each tool exposes a specific database operation scoped to the workspace's database.
    /// </summary>
    public interface IDatabaseToolService
    {
        /// <summary>List all table names in the database.</summary>
        Task<string> ListTablesAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default);

        /// <summary>Describe columns of a specific table.</summary>
        Task<string> DescribeTableAsync(string databaseName, DatabaseEngine engine, string connectionString, string tableName, CancellationToken cancellationToken = default);

        /// <summary>Search for tables/columns matching a keyword.</summary>
        Task<string> SearchSchemaAsync(string databaseName, DatabaseEngine engine, string connectionString, string query, CancellationToken cancellationToken = default);

        /// <summary>List all views with their definitions.</summary>
        Task<string> ListViewsAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default);

        /// <summary>List all stored procedures and functions with definitions.</summary>
        Task<string> ListRoutinesAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute a read-only SELECT query. Returns column metadata and up to 100 rows.
        /// Non-SELECT queries are rejected.
        /// </summary>
        Task<string> ExecuteReadOnlyQueryAsync(string databaseName, DatabaseEngine engine, string connectionString, string sql, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute any SQL statement targeting objects inside the current workspace database
        /// (INSERT, UPDATE, DELETE, CREATE TABLE, ALTER TABLE, DROP TABLE, TRUNCATE,
        ///  CREATE/ALTER/DROP PROCEDURE/VIEW/INDEX, EXEC/CALL for local stored procedures).
        /// Database-level operations (DROP DATABASE, CREATE DATABASE, ALTER DATABASE),
        /// cross-database USE statements, GRANT/REVOKE, and xp_ system procedures are blocked.
        /// Returns rows-affected count on success or an error message.
        /// </summary>
        Task<string> ExecuteWriteAsync(string databaseName, DatabaseEngine engine, string connectionString, string sql, CancellationToken cancellationToken = default);
    }
}
