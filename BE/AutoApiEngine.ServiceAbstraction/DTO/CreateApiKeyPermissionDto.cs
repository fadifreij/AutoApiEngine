namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Request body for creating one API key scope.
    /// <para>
    /// Deliberately has NO OrganizationId: the organization comes from the JWT claim only. A
    /// caller-supplied org id would be a cross-org grant waiting to happen.
    /// </para>
    /// </summary>
    public class CreateApiKeyPermissionDto
    {
        /// <summary>Object name, or null for the "any object" wildcard. An empty string is NOT a wildcard.</summary>
        public string? ObjectName { get; set; }

        /// <summary>GET | POST | PUT | DELETE, or null for the "any verb" wildcard.</summary>
        public string? Verb { get; set; }

        public Guid WorkspaceId { get; set; }

        /// <summary>Optional — the backend fills it from Workspace.DatabaseName when omitted.</summary>
        public string? DatabaseName { get; set; }

        public bool IsDeny { get; set; } = false;
    }
}
