namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// A single chunk emitted while streaming an AI assistant reply.
    /// Either carries a text <see cref="Delta"/>, a terminal <see cref="Done"/> signal,
    /// or an <see cref="Error"/>.
    /// </summary>
    public class AiStreamChunk
    {
        /// <summary>Incremental text appended to the reply.</summary>
        public string? Delta { get; set; }

        /// <summary>True when the stream has finished successfully.</summary>
        public bool Done { get; set; }

        /// <summary>Set when the stream ends due to an error.</summary>
        public string? Error { get; set; }

        /// <summary>Model identifier (sent on the final chunk).</summary>
        public string? Model { get; set; }
    }
}
