namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class DdlExecutionRequest
    {
        public string WorkspaceId { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
    }
}