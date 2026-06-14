namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Request sent to the AI database assistant.
    /// The assistant is scoped to helping with DDL authoring, query/procedure
    /// optimization, indexing and performance for the workspace's current database.
    /// It only returns advice/SQL text — it never executes anything.
    /// </summary>
    public class AiAssistRequest
    {
        /// <summary>The workspace whose database schema gives the assistant context.</summary>
        public string WorkspaceId { get; set; } = string.Empty;

        /// <summary>The user's latest question/prompt.</summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>
        /// Optional prior conversation turns for multi-turn context.
        /// Only "user" and "assistant" roles are honored; the system prompt is added server-side.
        /// </summary>
        public List<AiChatMessage> History { get; set; } = new();

        /// <summary>
        /// Optional SQL currently in the editor — supplied so the assistant can
        /// review / optimize the exact query the user is working on.
        /// </summary>
        public string? CurrentSql { get; set; }
    }

    /// <summary>A single chat message exchanged with the assistant.</summary>
    public class AiChatMessage
    {
        /// <summary>"user" or "assistant".</summary>
        public string Role { get; set; } = "user";

        /// <summary>The message text.</summary>
        public string Content { get; set; } = string.Empty;
    }
}
