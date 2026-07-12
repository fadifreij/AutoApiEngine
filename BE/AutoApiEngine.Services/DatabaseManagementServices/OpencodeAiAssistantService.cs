using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// AI assistant implementation that calls the local OpenCode server's native
    /// session API directly (port 3000 by default) using the Big Pickle LLM
    /// (model ID: opencode/big-pickle).
    ///
    /// Instead of OpenAI-compatible function calling, tool desciptions are embedded
    /// in the system prompt as JSON. The AI responds with JSON code blocks that the
    /// service parses to execute database tools (list_tables, describe_table, etc.).
    ///
    /// This avoids an extra proxy hop and communicates directly with OpenCode's
    /// HTTP server at http://127.0.0.1:{Port}.
    /// </summary>
    public class OpencodeAiAssistantService : IAiAssistantService
    {
        private readonly HttpClient _httpClient;
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IDatabaseToolService _databaseTools;
        private readonly OpencodeAiSettings _settings;
        private readonly ILogger<OpencodeAiAssistantService> _logger;
        private readonly IConfiguration _configuration;

        private const int MaxToolRounds = 5;

        // OpenCode server API paths
        private string ServerUrl => _settings.BaseUrl.TrimEnd('/'); // e.g. http://127.0.0.1:3000
        private string SessionApi => $"{ServerUrl}/session";
        private string MessageApi(string sessionId) => $"{ServerUrl}/session/{sessionId}/message";

        // Basic auth for the OpenCode server
        private string AuthHeader => $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes($"opencode:{_settings.ApiKey ?? "local-dev-key"}"))}";

        public OpencodeAiAssistantService(
            HttpClient httpClient,
            IWorkspaceRepository workspaceRepository,
            IDatabaseToolService databaseTools,
            IOptions<OpencodeAiSettings> settings,
            IConfiguration configuration,
            ILogger<OpencodeAiAssistantService> logger)
        {
            _httpClient = httpClient;
            _workspaceRepository = workspaceRepository;
            _databaseTools = databaseTools;
            _settings = settings.Value;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<AiAssistResponse> AssistAsync(
            AiAssistRequest request,
            CancellationToken cancellationToken = default)
        {
            // ── Resolve workspace database info ──
            var (engine, databaseName, connectionString) = await ResolveDatabaseInfoAsync(request.WorkspaceId, cancellationToken);
            if (engine == null)
            {
                return new AiAssistResponse { Success = false, Error = "Workspace not found or has no database configured." };
            }

            // ── Ensure we have an OpenCode session ──
            var sessionId = await GetOrCreateSessionAsync(cancellationToken);
            if (sessionId == null)
            {
                return new AiAssistResponse
                {
                    Success = false,
                    Error = "Could not connect to the local OpenCode server. Ensure `opencode serve` is running."
                };
            }

            // ── Build messages ──
            // NOTE: Do NOT send a system role message via the session API — the Big Pickle
            // model treats session text as content, not instructions. System instructions
            // come from system-instructions.md loaded by the OpenCode server.
            // Instead, we prepend the database context to the first user message so the
            // AI knows which database it's targeting.
            var messages = new List<ChatMessage>();

            foreach (var msg in request.History ?? new List<AiChatMessage>())
            {
                var role = msg.Role?.Trim().ToLowerInvariant();
                if ((role == "user" || role == "assistant") && !string.IsNullOrWhiteSpace(msg.Content))
                {
                    messages.Add(new ChatMessage(role, msg.Content));
                }
            }

            var dbContext = BuildDatabaseContextPrefix(engine.Value, databaseName!);
            var userContent = new StringBuilder(dbContext);
            userContent.Append(request.Prompt?.Trim() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(request.CurrentSql))
            {
                userContent.Append("\n\n-- SQL currently in my editor --\n```sql\n")
                           .Append(request.CurrentSql!.Trim())
                           .Append("\n```");
            }
            messages.Add(new ChatMessage("user", userContent.ToString()));

            // ── Multi-round tool-calling loop ──
            bool dbChanged = false;
            int sentMessageCount = 0; // OpenCode keeps session state, so only send new messages each round
            for (int round = 0; round < MaxToolRounds; round++)
            {
                // Translate only unsent messages → OpenCode parts (avoids duplicating history)
                var newMessages = messages.Skip(sentMessageCount).ToList();
                var parts = ConvertMessagesToParts(newMessages);
                sentMessageCount = messages.Count;

                // Send to OpenCode server
                var ocResponse = await SendToOpenCodeAsync(sessionId, parts, cancellationToken);
                if (ocResponse == null)
                {
                    return new AiAssistResponse
                    {
                        Success = false,
                        Error = "The local OpenCode server did not respond in time. Verify it is running on port 3000."
                    };
                }

                var replyText = ocResponse.Text;
                if (string.IsNullOrWhiteSpace(replyText))
                {
                    return new AiAssistResponse { Success = false, Error = "The AI returned an empty response." };
                }

                // Check for tool calls embedded as JSON blocks in the text
                var toolCalls = ExtractToolCallsFromText(replyText);

                if (toolCalls.Count > 0)
                {
                    // Add assistant message with the text (minus the tool call JSON)
                    var cleanText = RemoveToolCallBlocks(replyText);
                    if (!string.IsNullOrWhiteSpace(cleanText))
                    {
                        messages.Add(new ChatMessage("assistant", cleanText));
                    }

                    // Execute each tool call
                    foreach (var tc in toolCalls)
                    {
                        var result = await ExecuteNamedToolAsync(tc.Name, tc.Arguments, databaseName!, engine!.Value, connectionString!, cancellationToken);
                        messages.Add(new ChatMessage("tool", result, tc.Id));

                        // Track successful execute_write calls — this means the database was modified
                        if (tc.Name == "execute_write" &&
                            result.StartsWith("Success:", StringComparison.OrdinalIgnoreCase))
                        {
                            dbChanged = true;
                        }
                    }
                    continue;
                }

                // No tool calls — this is the final answer
                return new AiAssistResponse
                {
                    Success = true,
                    Reply = replyText,
                    Model = "opencode/big-pickle",
                    DbChanged = dbChanged
                };
            }

            return new AiAssistResponse
            {
                Success = false,
                Error = $"The AI did not produce a final answer after {MaxToolRounds} rounds of tool calls. Please try a simpler question."
            };
        }

        public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
            AiAssistRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Use a channel to decouple SSE reader (producer) from yield loop (consumer)
            var channel = Channel.CreateUnbounded<AiStreamChunk>();

            // Run the streaming logic in a background task that writes to the channel
            var streamingTask = Task.Run(async () =>
            {
                try
                {
                    await StreamFromOpenCodeAsync(request, channel.Writer, cancellationToken);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, cancellationToken);

            // Yield from the channel (no try-catch around yield)
            await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return chunk;
            }

            // Ensure the background task completes
            await streamingTask;
        }

        /// <summary>
        /// Core streaming logic: sends prompt to OpenCode, reads SSE events, writes chunks to channel.
        /// </summary>
        private async Task StreamFromOpenCodeAsync(
            AiAssistRequest request,
            ChannelWriter<AiStreamChunk> writer,
            CancellationToken ct)
        {
            // ── Resolve workspace database info ──
            var (engine, databaseName, connectionString) = await ResolveDatabaseInfoAsync(request.WorkspaceId, ct);
            if (engine == null)
            {
                await writer.WriteAsync(new AiStreamChunk { Error = "Workspace not found or has no database configured." }, ct);
                return;
            }

            // ── Ensure we have an OpenCode session ──
            var sessionId = await GetOrCreateSessionAsync(ct);
            if (sessionId == null)
            {
                await writer.WriteAsync(new AiStreamChunk
                {
                    Error = "Could not connect to the local OpenCode server. Ensure `opencode serve` is running."
                }, ct);
                return;
            }

            // ── Build messages ──
            // NOTE: Do NOT send a system role message — Big Pickle treats session text as
            // content, not instructions. System instructions come from system-instructions.md.
            // We prepend DB context to the first user message instead.
            var messages = new List<ChatMessage>();

            foreach (var msg in request.History ?? new List<AiChatMessage>())
            {
                var role = msg.Role?.Trim().ToLowerInvariant();
                if ((role == "user" || role == "assistant") && !string.IsNullOrWhiteSpace(msg.Content))
                    messages.Add(new ChatMessage(role, msg.Content));
            }

            var dbContext = BuildDatabaseContextPrefix(engine.Value, databaseName!);
            var userContent = new StringBuilder(dbContext);
            userContent.Append(request.Prompt?.Trim() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(request.CurrentSql))
            {
                userContent.Append("\n\n-- SQL currently in my editor --\n```sql\n")
                           .Append(request.CurrentSql!.Trim())
                           .Append("\n```");
            }
            messages.Add(new ChatMessage("user", userContent.ToString()));

            // ── Multi-round tool-calling loop with REAL streaming ──
            bool dbChanged = false;
            int sentMessageCount = 0; // OpenCode keeps session state, so only send new messages each round
            for (int round = 0; round < MaxToolRounds; round++)
            {
                // Only send messages not yet sent to the session (avoids duplicating history)
                var newMessages = messages.Skip(sentMessageCount).ToList();
                var parts = ConvertMessagesToParts(newMessages);
                sentMessageCount = messages.Count;
                var fullText = new StringBuilder();
                string? modelId = "opencode/big-pickle";

                var roundCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                roundCts.CancelAfter(TimeSpan.FromMinutes(3));

                try
                {
                    // 1. Connect to SSE endpoint first
                    _logger.LogDebug("Connecting to OpenCode SSE event stream for session {SessionId}", sessionId);
                    using var sseClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                    sseClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", AuthHeader);

                    using var sseResponse = await sseClient.GetAsync(
                        $"{ServerUrl}/event",
                        HttpCompletionOption.ResponseHeadersRead,
                        roundCts.Token);
                    sseResponse.EnsureSuccessStatusCode();
                    _logger.LogDebug("SSE connection established for session {SessionId}", sessionId);

                    using var stream = await sseResponse.Content.ReadAsStreamAsync(roundCts.Token);
                    using var reader = new StreamReader(stream);

                    // 2. Send prompt and wait for confirmation
                    var promptSent = await SendPromptAsync(sessionId, parts, roundCts.Token);
                    if (!promptSent)
                    {
                        await writer.WriteAsync(new AiStreamChunk { Error = "Failed to send prompt to OpenCode server." }, ct);
                        return;
                    }

                    // 3. Read SSE events until session.idle or cancellation
                    bool receivedFirstEvent = false;

                    while (!roundCts.Token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(roundCts.Token);
                        if (line == null) break; // EOF
                        if (string.IsNullOrEmpty(line)) continue;
                        if (!line.StartsWith("data: ")) continue;

                        var json = line["data: ".Length..].Trim();
                        if (string.IsNullOrEmpty(json)) continue;

                        // Log first few raw events for debugging
                        if (!receivedFirstEvent)
                        {
                            _logger.LogDebug("First raw SSE event: {Json}", json.Length > 500 ? json[..500] + "..." : json);
                        }

                        if (!TryParseSseEvent(json, out var eventType, out var eventDoc, out var eventProps))
                        {
                            _logger.LogDebug("Failed to parse SSE event (type extraction failed)");
                            continue;
                        }

                        using (eventDoc) // Dispose the document after processing
                        {
                            // Filter events for our session
                            if (eventProps.TryGetProperty("sessionID", out var sidEl))
                            {
                                var eventSessionId = sidEl.GetString();
                                if (eventSessionId != sessionId)
                                {
                                    continue; // Silent skip for other sessions
                                }
                            }

                            // Log first relevant event for debugging
                            if (!receivedFirstEvent)
                            {
                                receivedFirstEvent = true;
                                _logger.LogDebug("First SSE event for session {SessionId}: type={EventType}", sessionId, eventType);
                            }

                            if (eventType == "message.part.updated")
                            {
                                if (!eventProps.TryGetProperty("part", out var part)) continue;
                                if (!part.TryGetProperty("type", out var pType) || pType.GetString() != "text") continue;

                                string? delta = null;

                                // Prefer delta field
                                if (eventProps.TryGetProperty("delta", out var deltaEl) && deltaEl.ValueKind == JsonValueKind.String)
                                {
                                    delta = deltaEl.GetString();
                                }
                                // Fallback: compute delta from full text
                                else if (part.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                                {
                                    var newText = textEl.GetString() ?? "";
                                    if (newText.Length > fullText.Length)
                                        delta = newText[fullText.Length..];
                                }

                                if (!string.IsNullOrEmpty(delta))
                                {
                                    fullText.Append(delta);
                                    await writer.WriteAsync(new AiStreamChunk { Delta = delta }, roundCts.Token);
                                }

                                // Get model info
                                if (eventProps.TryGetProperty("modelID", out var midEl))
                                    modelId = midEl.GetString() ?? modelId;
                            }
                            else if (eventType == "session.idle")
                            {
                                _logger.LogDebug("Session {SessionId} became idle, completing stream", sessionId);
                                break;
                            }
                        } // end using (eventDoc)
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("OpenCode SSE stream timed out for round {Round}", round);
                    await writer.WriteAsync(new AiStreamChunk { Error = "AI response timed out." }, ct);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SSE streaming error in round {Round}", round);
                    await writer.WriteAsync(new AiStreamChunk { Error = $"Streaming error: {ex.Message}" }, ct);
                    return;
                }

                var replyText = fullText.ToString();
                if (string.IsNullOrWhiteSpace(replyText))
                {
                    await writer.WriteAsync(new AiStreamChunk { Error = "The AI returned an empty response." }, ct);
                    return;
                }

                // Check for tool calls
                var toolCalls = ExtractToolCallsFromText(replyText);

                if (toolCalls.Count > 0)
                {
                    var cleanText = RemoveToolCallBlocks(replyText);
                    if (!string.IsNullOrWhiteSpace(cleanText))
                        messages.Add(new ChatMessage("assistant", cleanText));

                    foreach (var tc in toolCalls)
                    {
                        // Send progress update so the user knows we're executing tools
                        await writer.WriteAsync(new AiStreamChunk { Delta = $"\n\n🔧 *Executing {tc.Name}...*\n" }, ct);

                        var result = await ExecuteNamedToolAsync(tc.Name, tc.Arguments, databaseName!, engine!.Value, connectionString!, ct);
                        messages.Add(new ChatMessage("tool", result, tc.Id));

                        _logger.LogDebug("Tool {ToolName} executed, result length: {Length}", tc.Name, result.Length);

                        if (tc.Name == "execute_write" && result.StartsWith("Success:", StringComparison.OrdinalIgnoreCase))
                            dbChanged = true;
                    }
                    continue;
                }

                // Final answer
                await writer.WriteAsync(new AiStreamChunk { Done = true, Model = modelId, DbChanged = dbChanged }, ct);
                return;
            }

            await writer.WriteAsync(new AiStreamChunk
            {
                Error = $"The AI did not produce a final answer after {MaxToolRounds} rounds of tool calls."
            }, ct);
        }

        /// <summary>
        /// Parses a raw SSE data JSON string into event type and properties.
        /// OpenCode SSE format: {"payload":{"type":"event.type","properties":{...}}}
        /// Returns a cloned JsonDocument that the caller must dispose.
        /// </summary>
        private bool TryParseSseEvent(string json, out string? eventType, out JsonDocument? doc, out JsonElement props)
        {
            eventType = null;
            doc = null;
            props = default;

            try
            {
                doc = JsonDocument.Parse(json);

                // Try standard format: {"payload":{"type":"...", "properties":{...}}}
                if (doc.RootElement.TryGetProperty("payload", out var payload))
                {
                    eventType = payload.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                    if (payload.TryGetProperty("properties", out var p))
                        props = p;
                    return !string.IsNullOrEmpty(eventType);
                }

                // Alternative format: {"type":"...", "properties":{...}} (no payload wrapper)
                if (doc.RootElement.TryGetProperty("type", out var directType))
                {
                    eventType = directType.GetString();
                    if (doc.RootElement.TryGetProperty("properties", out var directProps))
                        props = directProps;
                    else if (doc.RootElement.TryGetProperty("data", out var dataProps))
                        props = dataProps;
                    return !string.IsNullOrEmpty(eventType);
                }

                _logger.LogDebug("Unknown SSE event format: {Json}", json.Length > 200 ? json[..200] : json);
                doc.Dispose();
                doc = null;
                return false;
            }
            catch (JsonException ex)
            {
                _logger.LogDebug("Failed to parse SSE JSON: {Error}", ex.Message);
                doc?.Dispose();
                doc = null;
                return false;
            }
        }

        /// <summary>
        /// Sends a prompt to OpenCode asynchronously via prompt_async endpoint.
        /// Returns true if the prompt was accepted, false otherwise.
        /// The response is received via the SSE /event stream.
        /// </summary>
        private async Task<bool> SendPromptAsync(string sessionId, List<OpenCodePart> parts, CancellationToken ct)
        {
            try
            {
                var bodyObj = new { parts, noReply = false };
                var json = JsonSerializer.Serialize(bodyObj, AiJsonOptions.Default);
                var url = $"{ServerUrl}/session/{sessionId}/prompt_async";

                _logger.LogDebug("Sending prompt to OpenCode session {SessionId}", sessionId);

                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("Authorization", AuthHeader);

                using var res = await _httpClient.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode)
                {
                    var errBody = await res.Content.ReadAsStringAsync(ct);
                    _logger.LogError("OpenCode prompt_async failed: {Status} — {Body}", (int)res.StatusCode, errBody);
                    return false;
                }

                _logger.LogDebug("Prompt accepted by OpenCode session {SessionId}", sessionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send prompt_async to OpenCode");
                return false;
            }
        }

        // ── OpenCode Server API Methods ──

        /// <summary>
        /// Creates a fresh OpenCode session for each request. A new session avoids
        /// conversation-history bleed between different workspaces/questions, which
        /// otherwise causes the AI to answer about the wrong database.
        /// </summary>
        private async Task<string?> GetOrCreateSessionAsync(CancellationToken ct)
        {
            try
            {
                var payload = "{}"; // Empty body — OpenCode creates a default session
                var req = new HttpRequestMessage(HttpMethod.Post, SessionApi)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("Authorization", AuthHeader);

                using var res = await _httpClient.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogError("OpenCode session creation failed: {Status}", (int)res.StatusCode);
                    return null;
                }

                var body = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                var sessionId = doc.RootElement.GetProperty("id").GetString();

                _logger.LogInformation("Created fresh OpenCode session: {SessionId}", sessionId);
                return sessionId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create OpenCode session at {Url}", SessionApi);
                return null;
            }
        }

        private async Task<OpenCodeResponse?> SendToOpenCodeAsync(
            string sessionId, List<OpenCodePart> parts, CancellationToken ct)
        {
            try
            {
                var bodyObj = new { parts, noReply = false };
                var json = JsonSerializer.Serialize(bodyObj, AiJsonOptions.Default);
                var req = new HttpRequestMessage(HttpMethod.Post, MessageApi(sessionId))
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("Authorization", AuthHeader);

                using var res = await _httpClient.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode)
                {
                    var errBody = await res.Content.ReadAsStringAsync(ct);
                    _logger.LogError("OpenCode message failed: {Status} — {Body}", (int)res.StatusCode, errBody);
                    return null;
                }

                var responseBody = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(responseBody);

                // Extract text parts
                var textParts = new List<string>();
                if (doc.RootElement.TryGetProperty("parts", out var partsArray))
                {
                    foreach (var part in partsArray.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var typeEl) &&
                            typeEl.GetString() == "text" &&
                            part.TryGetProperty("text", out var textEl))
                        {
                            textParts.Add(textEl.GetString() ?? "");
                        }
                    }
                }

                var modelId = "opencode/big-pickle";
                if (doc.RootElement.TryGetProperty("info", out var info))
                {
                    if (info.TryGetProperty("modelID", out var mid))
                        modelId = mid.GetString() ?? modelId;
                }

                return new OpenCodeResponse
                {
                    Text = string.Join("\n", textParts),
                    ModelId = modelId
                };
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("OpenCode request timed out for session {SessionId}", sessionId);
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error reaching OpenCode server at {Url}", ServerUrl);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenCode request failed unexpectedly");
                return null;
            }
        }

        // ── Message / Tool Translation ──

        /// <summary>
        /// Converts OpenAI-format ChatMessage list to OpenCode parts format.
        /// </summary>
        private static List<OpenCodePart> ConvertMessagesToParts(List<ChatMessage> messages)
        {
            var parts = new List<OpenCodePart>();

            foreach (var msg in messages)
            {
                if (msg.Role == "system")
                {
                    // Send system context without verbose prefix
                    parts.Add(new OpenCodePart("text", msg.Content ?? ""));
                }
                else if (msg.Role == "user")
                {
                    // Just send user message content directly
                    parts.Add(new OpenCodePart("text", msg.Content ?? ""));
                }
                else if (msg.Role == "assistant")
                {
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                    {
                        parts.Add(new OpenCodePart("text", msg.Content));
                    }
                    if (msg.ToolCalls != null)
                    {
                        foreach (var tc in msg.ToolCalls)
                        {
                            parts.Add(new OpenCodePart("text",
                                $"[Tool Call: {tc.Function?.Name ?? "unknown"}]\nArguments: {tc.Function?.Arguments ?? "{}"}"));
                        }
                    }
                }
                else if (msg.Role == "tool")
                {
                    parts.Add(new OpenCodePart("text",
                        $"Tool result:\n{msg.Content ?? "(empty)"}"));
                }
            }

            return parts;
        }

        /// <summary>
        /// Extracts tool calls from AI response text by looking for JSON with
        /// the format: {"tool": "tool_name", "arguments": {...}}
        /// Handles both code-fenced (```json ... ```) and raw JSON formats.
        /// </summary>
        private static List<ParsedToolCall> ExtractToolCallsFromText(string text)
        {
            var results = new List<ParsedToolCall>();
            if (string.IsNullOrWhiteSpace(text)) return results;

            // Pattern 1: Match JSON code blocks ```json ... ```
            var jsonBlockRegex = new Regex(@"```json\s*(\{.*?\})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var matches = jsonBlockRegex.Matches(text);

            foreach (Match match in matches)
            {
                TryParseToolCall(match.Groups[1].Value, results);
            }

            // Pattern 2: Match raw JSON tool calls (not in code blocks)
            // Look for {"tool": "...", "arguments": {...}}
            if (results.Count == 0)
            {
                var rawJsonRegex = new Regex(@"\{[^{}]*""tool""\s*:\s*""[^""]+""[^{}]*""arguments""\s*:\s*\{[^{}]*\}[^{}]*\}", RegexOptions.Singleline);
                var rawMatches = rawJsonRegex.Matches(text);

                foreach (Match match in rawMatches)
                {
                    TryParseToolCall(match.Value, results);
                }
            }

            // Pattern 3: Simple fallback - try to parse the entire text as JSON if it looks like a tool call
            if (results.Count == 0 && text.TrimStart().StartsWith("{") && text.Contains("\"tool\""))
            {
                TryParseToolCall(text.Trim(), results);
            }

            return results;
        }

        /// <summary>
        /// Attempts to parse a JSON string as a tool call and adds it to the results list.
        /// </summary>
        private static void TryParseToolCall(string json, List<ParsedToolCall> results)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("tool", out var toolEl) &&
                    root.TryGetProperty("arguments", out var argsEl))
                {
                    results.Add(new ParsedToolCall
                    {
                        Id = $"tc_{Guid.NewGuid():N}"[..20],
                        Name = toolEl.GetString() ?? "",
                        Arguments = argsEl.GetRawText()
                    });
                }
            }
            catch (JsonException)
            {
                // Malformed JSON — skip
            }
        }

        /// <summary>
        /// Removes JSON tool call blocks from the text, leaving just the natural language content.
        /// Handles both code-fenced and raw JSON formats.
        /// </summary>
        private static string RemoveToolCallBlocks(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Remove code-fenced JSON blocks
            var cleaned = Regex.Replace(text, @"```json\s*\{.*?\}\s*```", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // Remove raw JSON tool calls
            cleaned = Regex.Replace(cleaned, @"\{[^{}]*""tool""\s*:\s*""[^""]+""[^{}]*""arguments""\s*:\s*\{[^{}]*\}[^{}]*\}", "", RegexOptions.Singleline);

            return cleaned.Trim();
        }

        // ── Tool Execution ──

        private async Task<string> ExecuteNamedToolAsync(
            string toolName, string argumentsJson, string databaseName,
            DatabaseEngine engine, string connectionString, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(argumentsJson)) argumentsJson = "{}";
                var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson, AiJsonOptions.Default);

                return toolName switch
                {
                    "list_tables" => await _databaseTools.ListTablesAsync(databaseName, engine, connectionString, ct),
                    "describe_table" => await ExecuteDescribeTableAsync(args, databaseName, engine, connectionString, ct),
                    "search_schema" => await ExecuteSearchSchemaAsync(args, databaseName, engine, connectionString, ct),
                    "list_views" => await _databaseTools.ListViewsAsync(databaseName, engine, connectionString, ct),
                    "list_routines" => await _databaseTools.ListRoutinesAsync(databaseName, engine, connectionString, ct),
                    "execute_query" => await ExecuteReadOnlyQueryAsync(args, databaseName, engine, connectionString, ct),
                    "execute_write" => await ExecuteWriteAsync(args, databaseName, engine, connectionString, ct),
                    _ => $"Error: Unknown tool '{toolName}'."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool '{Name}' execution failed", toolName);
                return $"Error executing '{toolName}': {ex.Message}";
            }
        }

        private async Task<string> ExecuteWriteAsync(
            JsonElement args, string databaseName, DatabaseEngine engine,
            string connectionString, CancellationToken ct)
        {
            var sql = GetStringProperty(args, "sql");
            return string.IsNullOrWhiteSpace(sql)
                ? "Error: Missing 'sql' argument."
                : await _databaseTools.ExecuteWriteAsync(databaseName, engine, connectionString, sql, ct);
        }

        private async Task<string> ExecuteDescribeTableAsync(
            JsonElement args, string databaseName, DatabaseEngine engine,
            string connectionString, CancellationToken ct)
        {
            var tableName = GetStringProperty(args, "table_name");
            return string.IsNullOrWhiteSpace(tableName)
                ? "Error: Missing 'table_name' argument."
                : await _databaseTools.DescribeTableAsync(databaseName, engine, connectionString, tableName, ct);
        }

        private async Task<string> ExecuteSearchSchemaAsync(
            JsonElement args, string databaseName, DatabaseEngine engine,
            string connectionString, CancellationToken ct)
        {
            var query = GetStringProperty(args, "query");
            return string.IsNullOrWhiteSpace(query)
                ? "Error: Missing 'query' argument."
                : await _databaseTools.SearchSchemaAsync(databaseName, engine, connectionString, query, ct);
        }

        private async Task<string> ExecuteReadOnlyQueryAsync(
            JsonElement args, string databaseName, DatabaseEngine engine,
            string connectionString, CancellationToken ct)
        {
            var sql = GetStringProperty(args, "sql");
            return string.IsNullOrWhiteSpace(sql)
                ? "Error: Missing 'sql' argument."
                : await _databaseTools.ExecuteReadOnlyQueryAsync(databaseName, engine, connectionString, sql, ct);
        }

        private static string GetStringProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString() ?? ""
                : "";
        }

        // ── Workspace / Connection Helpers ──

        private async Task<(DatabaseEngine? engine, string? databaseName, string? connectionString)>
            ResolveDatabaseInfoAsync(string workspaceId, CancellationToken cancellationToken)
        {
            try
            {
                var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken);
                if (workspace == null || string.IsNullOrWhiteSpace(workspace.DatabaseName))
                    return (null, null, null);

                var connStr = BuildConnectionString(workspace.DatabaseEngine, workspace);
                return (workspace.DatabaseEngine, workspace.DatabaseName, connStr);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve database info for workspace {WorkspaceId}.", workspaceId);
                return (null, null, null);
            }
        }

        private string BuildConnectionString(DatabaseEngine engine, Domain.Entities.Workspace workspace)
        {
            var databaseName = workspace.DatabaseName ?? "";
            var baseConnStr = engine switch
            {
                DatabaseEngine.MySql => _configuration.GetConnectionString("MySqlConnection")
                    ?? "Server=localhost;Port=3307;Uid=root;Pwd=root;",
                _ => _configuration.GetConnectionString("SqlServerConnection")
                    ?? "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True;"
            };

            if (!string.IsNullOrWhiteSpace(workspace.DbUserName))
            {
                if (engine == DatabaseEngine.MySql)
                {
                    return $"{baseConnStr.TrimEnd(';')};Database={databaseName};Uid={workspace.DbUserName};Pwd={workspace.DbPassword ?? ""}";
                }
                else
                {
                    var csb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConnStr)
                    {
                        InitialCatalog = databaseName,
                        UserID = workspace.DbUserName,
                        Password = workspace.DbPassword ?? ""
                    };
                    return csb.ConnectionString;
                }
            }

            if (engine == DatabaseEngine.MySql)
            {
                return $"{baseConnStr.TrimEnd(';')};Database={databaseName}";
            }

            var sqlCsb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConnStr)
            {
                InitialCatalog = databaseName
            };
            return sqlCsb.ConnectionString;
        }

        // ── Database Context Helper ──

        /// <summary>
        /// Builds a short database context prefix prepended to the first user message.
        /// This tells the Big Pickle model which database engine and name it is working with,
        /// so it targets the correct workspace database and uses the right SQL dialect.
        ///
        /// IMPORTANT: This is NOT a system prompt — it's embedded in the user message because
        /// the Big Pickle model treats session API text as content, not instructions.
        /// Actual instructions come from system-instructions.md loaded by the OpenCode server.
        /// </summary>
        private static string BuildDatabaseContextPrefix(DatabaseEngine engine, string databaseName)
        {
            var engineName = engine switch
            {
                DatabaseEngine.SqlServer => "SQL Server",
                DatabaseEngine.MySql => "MySQL",
                DatabaseEngine.PostgreSql => "PostgreSQL",
                DatabaseEngine.Sqlite => "SQLite",
                _ => engine.ToString()
            };

            return $"[Workspace Database: {engineName} / {databaseName}] — Use the database tools to query this database. All SQL must use {engineName} syntax.\n\n";
        }

        // ── Internal Types ──

        private sealed class ParsedToolCall
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Arguments { get; set; } = "{}";
        }

        private sealed class OpenCodeResponse
        {
            public string Text { get; set; } = "";
            public string ModelId { get; set; } = "opencode/big-pickle";
        }

        private sealed class OpenCodePart
        {
            public string Type { get; set; }
            public string Text { get; set; }

            public OpenCodePart(string type, string text)
            {
                Type = type;
                Text = text;
            }
        }
    }
}
