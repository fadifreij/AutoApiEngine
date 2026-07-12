using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.ServiceAbstraction
{
    public interface IDeployedApiService
    {
        /// <summary>Save a new deployed API configuration.</summary>
        Task<DeployedApiDto> DeployAsync(DeployApiRequest request, CancellationToken ct = default);

        /// <summary>List all deployed APIs for a workspace.</summary>
        Task<List<DeployedApiDto>> ListByWorkspaceAsync(string workspaceId, CancellationToken ct = default);

        /// <summary>Delete a deployed API by its ID.</summary>
        Task DeleteAsync(string id, CancellationToken ct = default);

        /// <summary>Execute a deployed API and return the results.</summary>
        Task<TestDeployedApiResponse> TestAsync(string deployedApiId, CancellationToken ct = default);
    }
}
