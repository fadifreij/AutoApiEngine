namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class RegisterResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? OrganizationId { get; set; }
    }
}
