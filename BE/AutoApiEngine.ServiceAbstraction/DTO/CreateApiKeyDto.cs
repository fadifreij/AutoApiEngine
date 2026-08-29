namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class CreateApiKeyDto
    {
        public string Name { get; set; } = string.Empty;
        public Guid OrganizationId { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}