namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Configuration for the local Opencode AI provider.
    /// Bound from the "OpencodeAi" configuration section.
    /// Uses the OpenCode proxy server which translates between OpenAI-compatible
    /// Chat Completions API and the local OpenCode server's session API.
    /// The default model is "opencode/big-pickle" (Big Pickle LLM).
    /// </summary>
    public class OpencodeAiSettings
    {
        /// <summary>
        /// Base URL of the local OpenCode server (NOT a proxy).
        /// Defaults to http://127.0.0.1:3000 (opencode serve default port).
        /// The service calls the OpenCode session API directly.
        /// </summary>
        public string BaseUrl { get; set; } = "http://127.0.0.1:3000";

        /// <summary>
        /// Optional API key for the OpenCode proxy (if required).
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Model identifier. The Big Pickle LLM is the default.
        /// Model ID format: opencode/big-pickle.
        /// </summary>
        public string Model { get; set; } = "opencode/big-pickle";

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
