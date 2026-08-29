namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class UpdateApiKeyDto
    {
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime? ExpiresAt { get; set; }
    }
}