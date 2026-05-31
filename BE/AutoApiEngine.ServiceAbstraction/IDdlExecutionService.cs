using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.ServiceAbstraction
{
    public interface IDdlExecutionService
    {
        Task<DdlExecutionResponse> ExecuteAsync(DdlExecutionRequest request, CancellationToken cancellationToken = default);
    }
}