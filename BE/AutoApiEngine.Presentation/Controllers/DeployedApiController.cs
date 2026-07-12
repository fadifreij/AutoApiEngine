using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoApiEngine.Presentation.Controllers
{
    /// <summary>
    /// Manages deployed (saved) dynamic API configurations.
    /// Allows users to save, list, delete, and test API configurations
    /// that target specific database objects with pre-configured filters, sorts, and column selection.
    /// </summary>
    [ApiController]
    [Route("api/deployed")]
    [Authorize]
    public class DeployedApiController : BaseController
    {
        private readonly IDeployedApiService _deployedApiService;

        public DeployedApiController(IDeployedApiService deployedApiService)
        {
            _deployedApiService = deployedApiService;
        }

        /// <summary>Deploy (save) a new API configuration.</summary>
        [HttpPost]
        public async Task<IActionResult> Deploy([FromBody] DeployApiRequest request, CancellationToken cancellationToken)
        {
            return await HandleRequestAsync(() =>
                _deployedApiService.DeployAsync(request, cancellationToken));
        }

        /// <summary>List all deployed APIs for a workspace.</summary>
        [HttpGet("workspace/{workspaceId:guid}")]
        public async Task<IActionResult> ListByWorkspace(string workspaceId, CancellationToken cancellationToken)
        {
            return await HandleRequestAsync(() =>
                _deployedApiService.ListByWorkspaceAsync(workspaceId, cancellationToken));
        }

        /// <summary>Delete (soft) a deployed API.</summary>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
        {
            return await HandleRequestAsync(async () =>
            {
                await _deployedApiService.DeleteAsync(id, cancellationToken);
                return new { message = "Deployed API deleted successfully." };
            });
        }

        /// <summary>Test a deployed API by executing it and returning the results.</summary>
        [HttpGet("{id:guid}/test")]
        public async Task<IActionResult> Test(string id, CancellationToken cancellationToken)
        {
            return await HandleRequestAsync(() =>
                _deployedApiService.TestAsync(id, cancellationToken));
        }
    }
}
