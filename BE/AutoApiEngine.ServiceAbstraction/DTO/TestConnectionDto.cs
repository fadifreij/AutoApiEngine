namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class TestConnectionDto
    {
        public string DatabaseEngine { get; set; } = "SqlServer";
        public string ServerHost { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? DatabaseName { get; set; }
    }
}
