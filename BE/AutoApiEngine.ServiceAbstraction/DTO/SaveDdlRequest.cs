namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Request to save a DDL/SQL file to the server's Saved_DDL folder.
    /// </summary>
    public class SaveDdlRequest
    {
        /// <summary>The workspace whose org/db determines the folder path.</summary>
        public string WorkspaceId { get; set; } = string.Empty;

        /// <summary>The user-chosen subfolder (e.g. "My Queries", "Project X").</summary>
        public string FolderName { get; set; } = string.Empty;

        /// <summary>The file name (e.g. "select-all.sql").</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>The SQL / DDL content to save.</summary>
        public string Content { get; set; } = string.Empty;
    }
}
