namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Top-level AI configuration. Only the provider selector lives here;
    /// each provider's specific settings (endpoint, model, API key, etc.)
    /// are in their own section (<c>"OpenRouterAi"</c> or <c>"OpencodeAi"</c>).
    /// Bound from the "Ai" configuration section.
    /// </summary>
    public class AiSettings
    {
        /// <summary>
        /// Selects which AI provider implementation to use at runtime.
        /// Supported values:
        ///   <c>"Opencode"</c> (default) — local model via <c>OpencodeAiSettings</c>
        ///   <c>"OpenRouter"</c> — external OpenAI-compatible API via <c>OpenRouterAiSettings</c>
        ///
        /// The strategy-pattern factory (<see cref="AiAssistantFactory"/>)
        /// reads this on every call so the provider can be switched via config hot-reload.
        /// </summary>
        public string Provider { get; set; } = "Opencode";
    }
}
