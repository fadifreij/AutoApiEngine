namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Configuration for the AI provider (Ollama, OpenRouter, or any OpenAI-compatible endpoint).
    /// Bound from the "OpenRouterAi" configuration section (name kept for backward compatibility).
    /// </summary>
    public class OpenRouterAiSettings
    {
        /// <summary>Base URL of the OpenAI-compatible endpoint (e.g. http://localhost:11434/v1 for Ollama).</summary>
        public string BaseUrl { get; set; } = "http://localhost:11434/v1";

        /// <summary>Provider API key. Ollama does not require a real key; any non-empty value works.</summary>
        public string ApiKey { get; set; } = "ollama";

        /// <summary>Model identifier to use for completions (e.g. qwen2.5:7b for Ollama).</summary>
        public string Model { get; set; } = "qwen2.5:3b";

        /// <summary>Sampling temperature (lower = more deterministic/faster).</summary>
        public double Temperature { get; set; } = 0.1;

        /// <summary>Maximum tokens to generate in the reply.</summary>
        public int MaxTokens { get; set; } = 1024;

        /// <summary>
        /// Ollama-specific: how long to keep the model loaded in RAM/VRAM after a request.
        /// "-1m" (any negative duration) keeps it resident indefinitely (fastest — no
        /// reload between turns), "30m" keeps it for 30 minutes, "0" unloads immediately.
        /// Must include a time unit; a bare "-1" is rejected by Ollama. Empty/null omits
        /// the field entirely (use the Ollama server default). Ignored by OpenRouter/OpenAI.
        /// </summary>
        public string? KeepAlive { get; set; } = "-1m";

        /// <summary>
        /// Maximum number of characters of database schema context to embed in the system
        /// prompt. Prevents the request from exceeding the model's context window (which
        /// causes a 400 from the provider) for databases with many tables/routines.
        /// Roughly 4 characters per token, so the default ~60k chars ≈ 15k tokens.
        /// </summary>
        public int MaxSchemaContextChars { get; set; } = 60000;
    }
}
