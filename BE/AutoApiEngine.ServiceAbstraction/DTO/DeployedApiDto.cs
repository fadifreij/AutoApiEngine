namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Request body for deploying (saving) a dynamic API configuration.
    /// </summary>
    public class DeployApiRequest
    {
        public string WorkspaceId { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string? SelectColumns { get; set; }
        public string? Filters { get; set; }
        public string? Sorts { get; set; }
        public int PageSize { get; set; } = 100;
    }

    /// <summary>
    /// Response returned when listing / viewing a deployed API.
    /// </summary>
    public class DeployedApiDto
    {
        public string Id { get; set; } = string.Empty;
        public string WorkspaceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string? SelectColumns { get; set; }
        public string? Filters { get; set; }
        public string? Sorts { get; set; }
        public int PageSize { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Request body for testing a deployed API.
    /// </summary>
    public class TestDeployedApiRequest
    {
        public string DeployedApiId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response returned when testing a deployed API (raw data).
    /// Uses the same shape as DynamicApiListResponse.
    /// </summary>
    public class TestDeployedApiResponse
    {
        public List<Dictionary<string, object?>> Data { get; set; } = new();
        public long? TotalCount { get; set; }
    }
}
