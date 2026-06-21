using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Strategy-pattern factory that selects the <see cref="IAiAssistantService"/>
    /// implementation at runtime based on the <see cref="AiSettings.Provider"/> value.
    ///
    /// <list type="bullet">
    ///   <item><c>"OpenRouter"</c> (default) → <see cref="AiAssistantService"/>  — external OpenAI-compatible API</item>
    ///   <item><c>"Opencode"</c> → <see cref="OpencodeAiAssistantService"/>        — local OpenAI-compatible model</item>
    /// </list>
    ///
    /// The check is case-insensitive and performed on every call so the provider
    /// can be changed via config hot-reload (<c>reloadOnChange: true</c>).
    /// </summary>
    public class AiAssistantFactory : IAiAssistantService
    {
        private readonly AiAssistantService _openRouterService;
        private readonly OpencodeAiAssistantService _opencodeService;
        private readonly AiSettings _settings;
        private readonly ILogger<AiAssistantFactory> _logger;

        public AiAssistantFactory(
            AiAssistantService openRouterService,
            OpencodeAiAssistantService opencodeService,
            IOptions<AiSettings> settings,
            ILogger<AiAssistantFactory> logger)
        {
            _openRouterService = openRouterService;
            _opencodeService = opencodeService;
            _settings = settings.Value;
            _logger = logger;
        }

        public Task<AiAssistResponse> AssistAsync(AiAssistRequest request, CancellationToken cancellationToken = default)
            => GetService().AssistAsync(request, cancellationToken);

        public IAsyncEnumerable<AiStreamChunk> StreamAsync(AiAssistRequest request, CancellationToken cancellationToken = default)
            => GetService().StreamAsync(request, cancellationToken);

        /// <summary>
        /// Resolves the active provider based on the current config value.
        /// Falls back to <see cref="AiAssistantService"/> (OpenRouter) for any unknown value.
        /// </summary>
        private IAiAssistantService GetService()
        {
            var provider = _settings.Provider?.Trim().ToLowerInvariant();

            switch (provider)
            {
                case "opencode":
                    _logger.LogDebug("AI provider: Opencode (local model)");
                    return _opencodeService;

                case "openrouter":
                default:
                    _logger.LogDebug("AI provider: OpenRouter");
                    return _openRouterService;
            }
        }
    }
}
