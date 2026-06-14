using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Resolves <see cref="ISchemaExplorerService"/> to the correct database-specific implementation.
    /// </summary>
    public class SchemaExplorerResolver : ISchemaExplorerService
    {
        private readonly SqlSchemaExplorer _sqlExplorer;
        private readonly MySqlSchemaExplorer _mySqlExplorer;

        public SchemaExplorerResolver(
            SqlSchemaExplorer sqlExplorer,
            MySqlSchemaExplorer mySqlExplorer)
        {
            _sqlExplorer = sqlExplorer;
            _mySqlExplorer = mySqlExplorer;
        }

        public Task<SchemaExplorerResponse> ExploreAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string? searchFilter = null,
            CancellationToken cancellationToken = default)
        {
            return engine switch
            {
                DatabaseEngine.MySql => _mySqlExplorer.ExploreAsync(databaseName, engine, connectionString, searchFilter, cancellationToken),
                _ => _sqlExplorer.ExploreAsync(databaseName, engine, connectionString, searchFilter, cancellationToken)
            };
        }

        public Task<List<TableSchemaDto>> GetTableColumnsAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            CancellationToken cancellationToken = default)
        {
            return engine switch
            {
                DatabaseEngine.MySql => _mySqlExplorer.GetTableColumnsAsync(databaseName, engine, connectionString, cancellationToken),
                _ => _sqlExplorer.GetTableColumnsAsync(databaseName, engine, connectionString, cancellationToken)
            };
        }

        public Task<List<RoutineDefinitionDto>> GetRoutineDefinitionsAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            CancellationToken cancellationToken = default)
        {
            return engine switch
            {
                DatabaseEngine.MySql => _mySqlExplorer.GetRoutineDefinitionsAsync(databaseName, engine, connectionString, cancellationToken),
                _ => _sqlExplorer.GetRoutineDefinitionsAsync(databaseName, engine, connectionString, cancellationToken)
            };
        }

        public Task<string> GetObjectDdlAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string objectName,
            string objectType,
            CancellationToken cancellationToken = default)
        {
            return engine switch
            {
                DatabaseEngine.MySql => _mySqlExplorer.GetObjectDdlAsync(databaseName, engine, connectionString, objectName, objectType, cancellationToken),
                _ => _sqlExplorer.GetObjectDdlAsync(databaseName, engine, connectionString, objectName, objectType, cancellationToken)
            };
        }
    }
}
