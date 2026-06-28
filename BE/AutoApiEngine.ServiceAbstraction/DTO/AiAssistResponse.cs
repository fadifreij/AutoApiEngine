namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Response returned by the AI database assistant.
    /// </summary>
    public class AiAssistResponse
    {
        /// <summary>Whether the assistant produced a reply successfully.</summary>
        public bool Success { get; set; }

        /// <summary>The assistant's reply (markdown). Populated when <c>Success</c> is true.</summary>
        public string Reply { get; set; } = string.Empty;

        /// <summary>Error message when <c>Success</c> is false.</summary>
        public string? Error { get; set; }

        /// <summary>The model that produced the reply (informational).</summary>
        public string? Model { get; set; }

        /// <summary>True when at least one execute_write tool call succeeded during the
        /// assistant's tool rounds — indicates the database was modified and the caller
        /// should refresh its schema / results.</summary>
        public bool DbChanged { get; set; }
    }
}
