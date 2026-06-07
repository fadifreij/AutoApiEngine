namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Request to delete a saved DDL/SQL file or folder from the server.
    /// </summary>
    public class DeleteDdlRequest
    {
        /// <summary>The full server path of the file or folder to delete.</summary>
        public string FilePath { get; set; } = string.Empty;
    }
}
