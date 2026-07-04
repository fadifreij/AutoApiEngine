namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Response returned for a list query (GET with possible filter/page params).
    /// </summary>
    public class DynamicApiListResponse
    {
        public List<Dictionary<string, object?>> Data { get; set; } = new();
        public DynamicApiPagingInfo? Paging { get; set; }
    }

    /// <summary>
    /// Response returned for a single-record query (GET by ID).
    /// </summary>
    public class DynamicApiSingleResponse
    {
        public Dictionary<string, object?>? Data { get; set; }
    }

    /// <summary>
    /// Response returned for create/update/delete actions.
    /// </summary>
    public class DynamicApiActionResponse
    {
        public Dictionary<string, object?>? Data { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Paging metadata included in list responses.
    /// </summary>
    public class DynamicApiPagingInfo
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public long TotalCount { get; set; }
        public int TotalPages { get; set; }
    }

    /// <summary>
    /// Error details returned for invalid requests.
    /// </summary>
    public class DynamicApiErrorResponse
    {
        public string Error { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public int StatusCode { get; set; } = 400;
    }
}
