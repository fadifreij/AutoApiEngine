namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Configuration for the AI assistant provider. Works with any OpenAI-compatible
    /// Chat Completions endpoint (OpenAI, Azure OpenAI, OpenRouter, Groq, Together, etc.).
    /// Bound from the "Ai" configuration section.
    /// </summary>
    public class AiSettings
    {
        /// <summary>Base URL of the OpenAI-compatible endpoint (e.g. https://openrouter.ai/api/v1).</summary>
        public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

        /// <summary>Provider API key. Prefer user-secrets / environment variables over appsettings.json.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Model identifier to use for completions (e.g. openai/gpt-oss-120b:free).</summary>
        public string Model { get; set; } = "openai/gpt-oss-120b:free";

        /// <summary>Sampling temperature.</summary>
        public double Temperature { get; set; } = 0.2;

        /// <summary>Maximum tokens to generate in the reply.</summary>
        public int MaxTokens { get; set; } = 1024;

        /// <summary>
        /// Maximum number of characters of database schema context to embed in the system
        /// prompt. Prevents the request from exceeding the model's context window (which
        /// causes a 400 from the provider) for databases with many tables/routines.
        /// Roughly 4 characters per token, so the default ~60k chars ≈ 15k tokens.
        /// </summary>
        public int MaxSchemaContextChars { get; set; } = 60000;
    }
}
