using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        // Reusable session ID (created once, reused across requests)
        private static string? _cachedSessionId;
        private static readonly object _sessionLock = new();

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

            // ── Build messages with tool definitions embedded as text ──
            var engineName = engine.ToString()!;
            var systemPrompt = BuildSystemPromptWithTools(engineName);
            var messages = new List<ChatMessage>
            {
                new("system", systemPrompt)
            };

            foreach (var msg in request.History ?? new List<AiChatMessage>())
            {
                var role = msg.Role?.Trim().ToLowerInvariant();
                if ((role == "user" || role == "assistant") && !string.IsNullOrWhiteSpace(msg.Content))
                {
                    messages.Add(new ChatMessage(role, msg.Content));
                }
            }

            var userContent = new StringBuilder(request.Prompt?.Trim() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(request.CurrentSql))
            {
                userContent.Append("\n\n-- SQL currently in my editor --\n```sql\n")
                           .Append(request.CurrentSql!.Trim())
                           .Append("\n```");
            }
            messages.Add(new ChatMessage("user", userContent.ToString()));

            // ── Multi-round tool-calling loop ──
            for (int round = 0; round < MaxToolRounds; round++)
            {
                // Translate OpenAI messages → OpenCode parts
                var parts = ConvertMessagesToParts(messages);

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
                    }
                    continue;
                }

                // No tool calls — this is the final answer
                return new AiAssistResponse
                {
                    Success = true,
                    Reply = replyText,
                    Model = "opencode/big-pickle"
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
            // For streaming, use the non-streaming AssistAsync and yield the result as a single chunk.
            // (The OpenCode server session API does not natively support SSE streaming.)
            var response = await AssistAsync(request, cancellationToken);
            if (!response.Success)
            {
                yield return new AiStreamChunk { Error = response.Error };
                yield break;
            }
            yield return new AiStreamChunk { Delta = response.Reply };
            yield return new AiStreamChunk { Done = true, Model = "opencode/big-pickle" };
        }

        // ── OpenCode Server API Methods ──

        private async Task<string?> GetOrCreateSessionAsync(CancellationToken ct)
        {
            // Return cached session if still valid
            if (_cachedSessionId != null)
            {
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, $"{SessionApi}/{_cachedSessionId}");
                    req.Headers.TryAddWithoutValidation("Authorization", AuthHeader);
                    var res = await _httpClient.SendAsync(req, ct);
                    if (res.IsSuccessStatusCode) return _cachedSessionId;
                }
                catch { /* Session expired, create new one */ }
            }

            lock (_sessionLock)
            {
                // Double-check after acquiring lock
                if (_cachedSessionId != null) return _cachedSessionId;
            }

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

                lock (_sessionLock)
                {
                    _cachedSessionId = sessionId;
                }

                _logger.LogInformation("Created OpenCode session: {SessionId}", sessionId);
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
        /// Builds the system prompt with tool definitions embedded as JSON text.
        /// The AI is instructed to output tool calls as ```json code blocks.
        /// </summary>
        private static string BuildSystemPromptWithTools(string engineName)
        {
            var basePrompt = AiPromptBuilder.BuildSystemPrompt(engineName);
            var tools = AiPromptBuilder.BuildToolDefinitions();
            var toolsJson = JsonSerializer.Serialize(tools, AiJsonOptions.Default);

            return $@"{basePrompt}

== TOOL CALLING FORMAT ==
You have the following database tools available. When you need to use a tool, output a JSON code block like this:

```json
{{""tool"": ""tool_name"", ""arguments"": {{""key"": ""value""}}}}
```

Available tools:
{toolsJson}

IMPORTANT:
- Output ONE tool call per JSON block.
- After receiving the tool result, continue the conversation naturally.
- When you have enough information to answer the user, provide a final response in plain text (no JSON block).
- Do NOT invent tool names — use only the tools listed above.";
        }

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
                    parts.Add(new OpenCodePart("text", $"[System Instruction]\n{msg.Content ?? ""}"));
                }
                else if (msg.Role == "user")
                {
                    parts.Add(new OpenCodePart("text", $"[User]\n{msg.Content ?? ""}"));
                }
                else if (msg.Role == "assistant")
                {
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                    {
                        parts.Add(new OpenCodePart("text", $"[Assistant]\n{msg.Content}"));
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
                        $"[Tool Result for {msg.ToolCallId ?? "tool"}]\n{msg.Content ?? "(empty)"}"));
                }
            }

            return parts;
        }

        /// <summary>
        /// Extracts tool calls from AI response text by looking for JSON code blocks
        /// with the format: {{"tool": "tool_name", "arguments": {{...}}}}
        /// </summary>
        private static List<ParsedToolCall> ExtractToolCallsFromText(string text)
        {
            var results = new List<ParsedToolCall>();
            if (string.IsNullOrWhiteSpace(text)) return results;

            // Match JSON code blocks ```json ... ```
            var jsonBlockRegex = new Regex(@"```json\s*(\{.*?\})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var matches = jsonBlockRegex.Matches(text);

            foreach (Match match in matches)
            {
                try
                {
                    var json = match.Groups[1].Value;
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
                    // Malformed JSON block — skip
                }
            }

            return results;
        }

        /// <summary>
        /// Removes JSON tool call blocks from the text, leaving just the natural language content.
        /// </summary>
        private static string RemoveToolCallBlocks(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var cleaned = Regex.Replace(text, @"```json\s*\{.*?\}\s*```", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
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
