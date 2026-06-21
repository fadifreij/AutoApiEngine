namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Configuration for the local Opencode AI provider.
    /// Bound from the "OpencodeAi" configuration section.
    /// Uses an OpenAI-compatible Chat Completions endpoint running locally
    /// (e.g. Ollama, LocalAI, or the Opencode inference server).
    /// Default model is "opencode/big-pickle".
    /// </summary>
    public class OpencodeAiSettings
    {
        /// <summary>
        /// Base URL of the local OpenAI-compatible endpoint.
        /// Defaults to http://localhost:11434/v1 (Ollama-compatible).
        /// </summary>
        public string BaseUrl { get; set; } = "http://localhost:11434/v1";

        /// <summary>
        /// Optional API key for the local endpoint (if required).
        /// Most local providers leave this empty.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Model identifier. Default: gemma4:latest.</summary>
        public string Model { get; set; } = "gemma4:latest";

        /// <summary>Sampling temperature (lower = more deterministic/faster).</summary>
        public double Temperature { get; set; } = 0.1;

        /// <summary>Maximum tokens to generate in the reply.</summary>
        public int MaxTokens { get; set; } = 1024;

        /// <summary>
        /// Maximum number of characters of database schema context to embed in the
        /// system prompt. Prevents requests from exceeding the model's context window.
        /// </summary>
        public int MaxSchemaContextChars { get; set; } = 60000;
    }
}
