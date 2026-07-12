using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.ServiceAbstraction
{
    /// <summary>
    /// Discovers foreign key relationships for a given table — both the FKs
    /// that originate from this table and the FKs from other tables that
    /// reference this table.
    /// </summary>
    public interface IForeignKeyService
    {
        /// <summary>
        /// Returns full FK metadata for a database table.
        /// </summary>
        /// <param name="databaseName">The target database.</param>
        /// <param name="engine">The database engine (SqlServer, MySql).</param>
        /// <param name="connectionString">Base connection string (without InitialCatalog/Database).</param>
        /// <param name="tableName">The table to inspect.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<ForeignKeyInfoDto> GetForeignKeysAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string tableName,
            CancellationToken cancellationToken = default);
    }
}
