namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// One API key scope as returned to the Scopes UI. ObjectName/Verb are nullable so the wildcard
    /// round-trips as null rather than being flattened into an empty string.
    /// </summary>
    public class ApiKeyPermissionDto
    {
        public Guid Id { get; set; }
        public Guid ApiKeyId { get; set; }
        public string? ApiKeyName { get; set; }
        public string? ObjectName { get; set; }
        public string? Verb { get; set; }
        public bool IsDeny { get; set; }
        public Guid WorkspaceId { get; set; }
        public string? WorkspaceName { get; set; }
        public string DatabaseName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
