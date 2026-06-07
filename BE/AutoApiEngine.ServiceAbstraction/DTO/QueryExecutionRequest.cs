namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Request to execute an arbitrary SQL query against a workspace's database.
    /// </summary>
    public class QueryExecutionRequest
    {
        /// <summary>Workspace whose database the query should run against.</summary>
        public string WorkspaceId { get; set; } = string.Empty;

        /// <summary>The SQL query text to execute.</summary>
        public string Sql { get; set; } = string.Empty;
    }
}
