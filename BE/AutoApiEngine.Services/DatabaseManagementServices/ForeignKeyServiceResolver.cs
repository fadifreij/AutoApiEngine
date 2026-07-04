using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Resolves <see cref="IForeignKeyService"/> to the correct database-specific implementation.
    /// </summary>
    public class ForeignKeyServiceResolver : IForeignKeyService
    {
        private readonly SqlForeignKeyService _sqlService;
        private readonly MySqlForeignKeyService _mySqlService;

        public ForeignKeyServiceResolver(
            SqlForeignKeyService sqlService,
            MySqlForeignKeyService mySqlService)
        {
            _sqlService = sqlService;
            _mySqlService = mySqlService;
        }

        public Task<ForeignKeyInfoDto> GetForeignKeysAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string tableName,
            CancellationToken cancellationToken = default)
        {
            return engine switch
            {
                DatabaseEngine.MySql => _mySqlService.GetForeignKeysAsync(databaseName, engine, connectionString, tableName, cancellationToken),
                _ => _sqlService.GetForeignKeysAsync(databaseName, engine, connectionString, tableName, cancellationToken)
            };
        }
    }
}
