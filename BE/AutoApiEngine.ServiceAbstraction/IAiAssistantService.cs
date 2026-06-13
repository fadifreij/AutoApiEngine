using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.ServiceAbstraction
{
    /// <summary>
    /// AI assistant scoped to helping users with the current workspace database:
    /// writing/refining DDL, optimizing queries and stored procedures, indexing
    /// and performance advice. It is advisory only and never executes SQL.
    /// </summary>
    public interface IAiAssistantService
    {
        Task<AiAssistResponse> AssistAsync(AiAssistRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Streams the assistant reply as incremental text chunks (Server-Sent Events style),
        /// so the UI can render tokens as they are generated instead of waiting for the full reply.
        /// </summary>
        IAsyncEnumerable<AiStreamChunk> StreamAsync(AiAssistRequest request, CancellationToken cancellationToken = default);
    }
}
