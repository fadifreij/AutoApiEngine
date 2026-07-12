using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.ServiceAbstraction
{
    /// <summary>
    /// Provides metadata about database objects for the UI — columns, types,
    /// primary keys, foreign keys, and objects that reference this table.
    /// </summary>
    public interface IDynamicApiMetadataService
    {
        /// <summary>
        /// Returns full metadata for a database object, including columns,
        /// primary key info, foreign keys (outgoing), and referenced-by (incoming).
        /// </summary>
        Task<DynamicApiObjectMetadataDto> GetObjectMetadataAsync(
            string workspaceId,
            string objectName,
            CancellationToken cancellationToken = default);
    }
}
