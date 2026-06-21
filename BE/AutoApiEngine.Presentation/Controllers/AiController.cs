using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AutoApiEngine.Presentation.Controllers
{
    /// <summary>
    /// AI database assistant endpoint. Provides advisory help for writing DDL,
    /// optimizing queries/procedures, and database performance for the current
    /// workspace. It is advisory only and never executes SQL.
    ///
    /// The AI provider is selected via <c>AiSettings.Provider</c> in appsettings.json.
    /// Supported values: "OpenRouter" (default) or "Opencode".
    /// </summary>
    [ApiController]
    [Route("api/ai")]
    [Authorize]
    public class AiController : BaseController
    {
        private readonly IAiAssistantService _aiAssistantService;
        private readonly AiSettings _aiSettings;
        private readonly OpenRouterAiSettings _openRouterSettings;
        private readonly OpencodeAiSettings _opencodeSettings;

        public AiController(
            IAiAssistantService aiAssistantService,
            IOptions<AiSettings> aiSettings,
            IOptions<OpenRouterAiSettings> openRouterSettings,
            IOptions<OpencodeAiSettings> opencodeSettings)
        {
            _aiAssistantService = aiAssistantService;
            _aiSettings = aiSettings.Value;
            _openRouterSettings = openRouterSettings.Value;
            _opencodeSettings = opencodeSettings.Value;
        }

        /// <summary>
        /// Returns the currently active AI provider and its model.
        /// Useful for the frontend to display which AI is powering the assistant.
        /// </summary>
        [HttpGet("provider")]
        public IActionResult GetProvider()
        {
            var isOpencode = string.Equals(_aiSettings.Provider, "Opencode", StringComparison.OrdinalIgnoreCase);

            if (isOpencode)
            {
                return Ok(new
                {
                    provider = _aiSettings.Provider,
                    model = _opencodeSettings.Model,
                    baseUrl = _opencodeSettings.BaseUrl,
                    configSection = "OpencodeAi"
                });
            }

            return Ok(new
            {
                provider = _aiSettings.Provider,
                model = _openRouterSettings.Model,
                baseUrl = _openRouterSettings.BaseUrl,
                configSection = "OpenRouterAi"
            });
        }

        /// <summary>
        /// Sends a prompt (plus optional history and the editor's current SQL) to the
        /// AI assistant and returns its advisory reply.
        /// </summary>
        [HttpPost("assist")]
        public async Task<IActionResult> Assist([FromBody] AiAssistRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.WorkspaceId))
                return BadRequest(new { message = "Workspace ID is required." });

            if (string.IsNullOrWhiteSpace(request.Prompt))
                return BadRequest(new { message = "Prompt cannot be empty." });

            var result = await _aiAssistantService.AssistAsync(request, cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Same as <see cref="Assist"/> but streams the reply token-by-token using
        /// Server-Sent Events so the UI can render text as it is generated.
        /// </summary>
        [HttpPost("assist/stream")]
        public async Task AssistStream([FromBody] AiAssistRequest request, CancellationToken cancellationToken)
        {
            Response.Headers["Content-Type"] = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            if (string.IsNullOrWhiteSpace(request.WorkspaceId) || string.IsNullOrWhiteSpace(request.Prompt))
            {
                await WriteSseAsync(new AiStreamChunk { Error = "Workspace ID and prompt are required." }, cancellationToken);
                return;
            }

            await foreach (var chunk in _aiAssistantService.StreamAsync(request, cancellationToken))
            {
                await WriteSseAsync(chunk, cancellationToken);
            }
        }

        private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

        private async Task WriteSseAsync(AiStreamChunk chunk, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(chunk, SseJsonOptions);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}
