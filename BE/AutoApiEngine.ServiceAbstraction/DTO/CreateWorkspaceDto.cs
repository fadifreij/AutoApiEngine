namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class CreateWorkspaceDto
    {
        public string Name { get; set; } = string.Empty;
        public string? EncryptionKey { get; set; } = string.Empty;
        public string? DbUserName { get; set; }
        public string? DbPassword { get; set; }
        public string? DatabaseName { get; set; }
        public string? DatabaseEngine { get; set; }
        public Guid OrganizationId { get; set; }
    }
}
