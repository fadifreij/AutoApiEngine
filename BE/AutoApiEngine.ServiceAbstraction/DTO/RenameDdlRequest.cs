namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Request to rename a saved DDL/SQL file or folder on the server.
    /// </summary>
    public class RenameDdlRequest
    {
        /// <summary>The full server path of the file or folder to rename.</summary>
        public string CurrentPath { get; set; } = string.Empty;

        /// <summary>The new name (not a full path — just the new file or folder name).</summary>
        public string NewName { get; set; } = string.Empty;
    }
}
