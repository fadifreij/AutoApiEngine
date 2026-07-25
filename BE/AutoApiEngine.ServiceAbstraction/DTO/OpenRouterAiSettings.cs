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

        /// <summary>Model identifier to use for completions (e.g. qwen2.5:3b for Ollama).</summary>
        public string Model { get; set; } = "qwen2.5:3b";

        /// <summary>Sampling temperature (lower = more deterministic/faster).</summary>
        public double Temperature { get; set; } = 0.1;

        /// <summary>Maximum tokens to generate in the reply.</summary>
        public int MaxTokens { get; set; } = 4096;

        /// <summary>
        /// Maximum number of characters of database schema context to embed in the system
        /// prompt. Prevents the request from exceeding the model's context window (which
        /// causes a 400 from the provider) for databases with many tables/routines.
        /// Roughly 4 characters per token, so the default ~60k chars ≈ 15k tokens.
        /// </summary>
        public int MaxSchemaContextChars { get; set; } = 60000;
    }
}
