using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.ServiceAbstraction
{
    /// <summary>
    /// Manages saved DDL/SQL files on the server file system.
    /// Files are stored under Saved_DDL/{orgName}/{dbName}/{userFolder}/*.sql.
    /// </summary>
    public interface IDdlFileService
    {
        /// <summary>
        /// Saves SQL content to Saved_DDL/{orgName}/{dbName}/{userFolder}/{fileName}.sql.
        /// Creates folders as needed.
        /// </summary>
        Task SaveAsync(SaveDdlRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the full folder + file tree for the given workspace.
        /// Only includes folders/files within that workspace's org/db path.
        /// </summary>
        Task<DdlFileTreeNode> GetTreeAsync(string workspaceId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the content of a saved file by its full server path.
        /// </summary>
        Task<string> ReadFileAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Renames a saved DDL file or folder on disk.
        /// </summary>
        Task RenameAsync(RenameDdlRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a saved DDL file or folder from disk.
        /// </summary>
        Task DeleteAsync(DeleteDdlRequest request, CancellationToken cancellationToken = default);
    }
}
