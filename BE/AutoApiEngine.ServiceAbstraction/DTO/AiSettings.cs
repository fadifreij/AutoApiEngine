namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Top-level AI configuration. Only the provider selector lives here;
    /// each provider's specific settings (endpoint, model, API key, etc.)
    /// are in their own section (<c>"OpenRouterAi"</c>).
    /// Bound from the "Ai" configuration section.
    /// </summary>
    public class AiSettings
    {
        /// <summary>
        /// Selects which AI provider implementation to use at runtime.
        /// Supported values:
        ///   <c>"Ollama"</c> (default) — local Ollama server via <c>OpenRouterAiSettings</c>
        ///   <c>"OpenRouter"</c> — external OpenAI-compatible API via <c>OpenRouterAiSettings</c>
        ///
        /// System instructions are loaded from <c>opencode/system-instructions.md</c>
        /// at startup and cached, making the AI assistant portable.
        /// </summary>
        public string Provider { get; set; } = "Ollama";
    }
}
