using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// AI assistant for the Query Studio. Calls an OpenAI-compatible chat-completions
    /// endpoint (OpenAI / Azure OpenAI / OpenRouter / etc.) and is tightly scoped — via
    /// the system prompt — to helping with DDL authoring, query/procedure optimization,
    /// indexing and performance for the workspace's current database.
    ///
    /// SAFETY: This service is advisory only. It returns text/SQL suggestions and
    /// NEVER executes anything against the database. The system prompt also instructs
    /// the model to refuse off-topic requests and to add explicit warnings before any
    /// destructive operation (DROP DATABASE/SCHEMA, TRUNCATE, unbounded DELETE/UPDATE).
    /// </summary>
    public class AiAssistantService : IAiAssistantService
    {
        private readonly HttpClient _httpClient;
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly ISchemaExplorerService _schemaExplorerService;
        private readonly AiSettings _settings;
        private readonly ILogger<AiAssistantService> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public AiAssistantService(
            HttpClient httpClient,
            IWorkspaceRepository workspaceRepository,
            ISchemaExplorerService schemaExplorerService,
            IOptions<AiSettings> settings,
            ILogger<AiAssistantService> logger)
        {
            _httpClient = httpClient;
            _workspaceRepository = workspaceRepository;
            _schemaExplorerService = schemaExplorerService;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<AiAssistResponse> AssistAsync(AiAssistRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                return new AiAssistResponse
                {
                    Success = false,
                    Error = "AI assistant is not configured. Set the Ai:ApiKey configuration value."
                };
            }

            // ── Gather database schema context (best-effort) ──
            var messages = await BuildMessagesAsync(request, cancellationToken);

            // ── Call the model ──
            try
            {
                var payload = new ChatCompletionRequest
                {
                    Model = _settings.Model,
                    Messages = messages,
                    Temperature = _settings.Temperature,
                    MaxTokens = _settings.MaxTokens,
                    Stream = false
                };

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions")
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

                using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("AI provider returned {Status}: {Body}", (int)httpResponse.StatusCode, body);
                    return new AiAssistResponse
                    {
                        Success = false,
                        Error = FormatProviderError((int)httpResponse.StatusCode, body)
                    };
                }

                var completion = await httpResponse.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, cancellationToken);
                var reply = completion?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

                if (string.IsNullOrWhiteSpace(reply))
                {
                    return new AiAssistResponse { Success = false, Error = "The AI returned an empty response." };
                }

                return new AiAssistResponse
                {
                    Success = true,
                    Reply = reply,
                    Model = completion?.Model ?? _settings.Model
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller (client) aborted the request — propagate.
                throw;
            }
            catch (TaskCanceledException ex)
            {
                // HttpClient timeout (not caller cancellation).
                _logger.LogError(ex, "AI request timed out after {Timeout}s contacting {BaseUrl}.", _httpClient.Timeout.TotalSeconds, _settings.BaseUrl);
                return new AiAssistResponse
                {
                    Success = false,
                    Error = $"The AI provider did not respond in time ({_httpClient.Timeout.TotalSeconds:N0}s). The endpoint '{_settings.BaseUrl}' may be unreachable from this server. Verify network access and the Ai:BaseUrl setting."
                };
            }
            catch (HttpRequestException ex)
            {
                // DNS failure, connection refused, TLS error, no route to host, etc.
                _logger.LogError(ex, "Network error reaching AI provider at {BaseUrl}.", _settings.BaseUrl);
                return new AiAssistResponse
                {
                    Success = false,
                    Error = $"Could not connect to the AI provider at '{_settings.BaseUrl}'. Check that the server has network access to this endpoint and that Ai:BaseUrl is correct."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI assistant request failed.");
                return new AiAssistResponse { Success = false, Error = "The AI service failed unexpectedly. Please try again." };
            }
        }

        public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
            AiAssistRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                yield return new AiStreamChunk { Error = "AI assistant is not configured. Set the Ai:ApiKey configuration value." };
                yield break;
            }

            var messages = await BuildMessagesAsync(request, cancellationToken);

            var payload = new ChatCompletionRequest
            {
                Model = _settings.Model,
                Messages = messages,
                Temperature = _settings.Temperature,
                MaxTokens = _settings.MaxTokens,
                Stream = true
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            HttpResponseMessage? httpResponse = null;
            string? startError = null;
            try
            {
                // ResponseHeadersRead lets us start reading the SSE body as soon as headers arrive.
                httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not start AI stream at {BaseUrl}.", _settings.BaseUrl);
                startError = $"Could not connect to the AI provider at '{_settings.BaseUrl}'.";
            }

            if (startError is not null)
            {
                yield return new AiStreamChunk { Error = startError };
                yield break;
            }

            using (httpResponse!)
            {
                if (!httpResponse!.IsSuccessStatusCode)
                {
                    var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("AI provider returned {Status}: {Body}", (int)httpResponse.StatusCode, body);
                    yield return new AiStreamChunk { Error = FormatProviderError((int)httpResponse.StatusCode, body) };
                    yield break;
                }

                await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);

                while (true)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }

                    if (line is null) break;
                    if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal)) continue;

                    var data = line["data:".Length..].Trim();
                    if (data.Length == 0) continue;
                    if (data == "[DONE]") break;

                    string? delta = null;
                    try
                    {
                        var chunk = JsonSerializer.Deserialize<StreamCompletionChunk>(data, JsonOptions);
                        delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
                    }
                    catch (JsonException)
                    {
                        // Ignore keep-alive comments / malformed partials.
                        continue;
                    }

                    if (!string.IsNullOrEmpty(delta))
                    {
                        yield return new AiStreamChunk { Delta = delta };
                    }
                }

                yield return new AiStreamChunk { Done = true, Model = _settings.Model };
            }
        }

        /// <summary>
        /// Loads workspace schema context (best-effort) and assembles the chat messages
        /// (system prompt + prior history + latest user prompt). Shared by both the
        /// blocking and streaming code paths.
        /// </summary>
        private async Task<List<ChatMessage>> BuildMessagesAsync(AiAssistRequest request, CancellationToken cancellationToken)
        {
            string schemaContext;
            string engineName = "SQL";
            try
            {
                var workspace = await _workspaceRepository.GetByIdAsync(request.WorkspaceId, cancellationToken);
                engineName = workspace.DatabaseEngine.ToString();

                if (!string.IsNullOrWhiteSpace(workspace.DatabaseName))
                {
                    var connectionString = workspace.DatabaseEngine switch
                    {
                        DatabaseEngine.MySql => "Server=localhost;Port=3307;Uid=root;Pwd=root;",
                        _ => "Server=LAPTOP-II43H7KF;Trusted_Connection=True;TrustServerCertificate=True;"
                    };

                    var schema = await _schemaExplorerService.ExploreAsync(
                        workspace.DatabaseName!,
                        workspace.DatabaseEngine,
                        connectionString,
                        searchFilter: null,
                        cancellationToken);

                    var tableColumns = await _schemaExplorerService.GetTableColumnsAsync(
                        workspace.DatabaseName!,
                        workspace.DatabaseEngine,
                        connectionString,
                        cancellationToken);

                    var routineDefinitions = await _schemaExplorerService.GetRoutineDefinitionsAsync(
                        workspace.DatabaseName!,
                        workspace.DatabaseEngine,
                        connectionString,
                        cancellationToken);

                    schemaContext = BuildSchemaContext(workspace.DatabaseName!, schema, tableColumns, routineDefinitions, _settings.MaxSchemaContextChars);
                }
                else
                {
                    schemaContext = "No database is associated with this workspace yet.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load schema context for AI assistant (workspace {WorkspaceId}).", request.WorkspaceId);
                schemaContext = "Database schema is currently unavailable.";
            }

            var messages = new List<ChatMessage>
            {
                new("system", BuildSystemPrompt(engineName, schemaContext))
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

            return messages;
        }

        private static string BuildSchemaContext(string databaseName, SchemaExplorerResponse schema, List<TableSchemaDto> tableColumns, List<RoutineDefinitionDto> routineDefinitions, int maxChars)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Database name: {databaseName}");
            sb.AppendLine($"Counts — Tables: {schema.TablesCount}, Views: {schema.ViewsCount}, Stored Procedures: {schema.StoredProceduresCount}, Functions: {schema.FunctionsCount}.");

            // Full table structures (every table with all of its columns).
            if (tableColumns.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Tables and columns (name type [PK] [NULL/NOT NULL]):");
                foreach (var table in tableColumns)
                {
                    var cols = table.Columns.Select(c =>
                    {
                        var flags = new List<string>();
                        if (c.IsPrimaryKey) flags.Add("PK");
                        flags.Add(c.IsNullable ? "NULL" : "NOT NULL");
                        return $"{c.Name} {c.DataType} [{string.Join(", ", flags)}]";
                    });
                    sb.AppendLine($"- {table.TableName}({string.Join(", ", cols)})");
                }
            }

            // Definitions of views, stored procedures, and functions.
            void AppendDefinitions(string label, string type)
            {
                var items = routineDefinitions
                    .Where(r => string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (items.Count == 0) return;

                sb.AppendLine();
                sb.AppendLine($"{label}:");
                foreach (var item in items)
                {
                    var def = item.Definition?.Trim();
                    if (string.IsNullOrEmpty(def))
                    {
                        sb.AppendLine($"- {item.Name} (definition unavailable)");
                    }
                    else
                    {
                        // Cap very large bodies to keep the prompt within token limits.
                        if (def.Length > 4000) def = def.Substring(0, 4000) + "\n-- ...(truncated)...";
                        sb.AppendLine($"- {item.Name}:");
                        sb.AppendLine("```sql");
                        sb.AppendLine(def);
                        sb.AppendLine("```");
                    }
                }
            }

            AppendDefinitions("Views (definitions)", "View");
            AppendDefinitions("Stored Procedures (definitions)", "StoredProcedure");
            AppendDefinitions("Functions (definitions)", "Function");

            // Guard against exceeding the model's context window: a database with many
            // tables/routines can otherwise produce a prompt larger than the provider
            // accepts, which returns a 400. Keep the highest-value content (table/column
            // structure comes first) and truncate the rest.
            if (maxChars > 0 && sb.Length > maxChars)
            {
                const string notice = "\n\n-- NOTE: schema context truncated to fit the model context window; some routine/view definitions were omitted. --";
                var keep = Math.Max(0, maxChars - notice.Length);
                return sb.ToString(0, keep) + notice;
            }

            return sb.ToString();
        }

        private static string BuildSystemPrompt(string engineName, string schemaContext)
        {
            return
$@"You are ""DB Copilot"", an expert database assistant embedded in a SQL Query Studio.
You help the user ONLY with topics related to their current database ({engineName}):
- Writing and refining DDL (CREATE/ALTER TABLE, indexes, constraints, views).
- Writing, explaining, and OPTIMIZING SELECT queries, joins, and stored procedures/functions.
- Performance tuning: indexing strategy, query plans, sargability, normalization, statistics.

STRICT RULES:
1. Stay on topic. If asked anything unrelated to this database, SQL, or database performance, politely decline and steer back to database help.
2. You are ADVISORY ONLY. You do not and cannot execute SQL — you only provide suggestions. The user runs queries themselves.
3. Tailor all SQL to the {engineName} dialect.
4. For any potentially DESTRUCTIVE or high-risk operation (DROP DATABASE, DROP SCHEMA, DROP TABLE, TRUNCATE, or DELETE/UPDATE without a WHERE clause), DO NOT present it casually. First add a clear ⚠️ warning, explain the impact and irreversibility, recommend a backup and a tested WHERE clause, and ask the user to confirm they really intend it. Never proactively suggest dropping the database or wiping data.
5. Prefer the smallest, safest change that solves the problem. Add brief comments to non-trivial SQL.
6. Keep answers concise and use markdown with ```sql code fences for SQL.

Use the following schema as context (do not invent tables/columns that are not listed):
{schemaContext}";
        }

        /// <summary>
        /// Builds a user-facing error message from a failed provider response, surfacing
        /// the provider's own error text (e.g. context-length-exceeded) when available so
        /// the problem is actionable instead of an opaque "please try again".
        /// </summary>
        private static string FormatProviderError(int status, string? body)
        {
            var detail = ExtractProviderMessage(body);
            return string.IsNullOrWhiteSpace(detail)
                ? $"AI provider error ({status}). Please try again."
                : $"AI provider error ({status}): {detail}";
        }

        private static string? ExtractProviderMessage(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.String) return error.GetString();
                    if (error.ValueKind == JsonValueKind.Object &&
                        error.TryGetProperty("message", out var message) &&
                        message.ValueKind == JsonValueKind.String)
                    {
                        return message.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // Body was not JSON; fall through.
            }

            var trimmed = body.Trim();
            return trimmed.Length > 300 ? trimmed[..300] + "…" : trimmed;
        }

        // ── Internal serialization types for the OpenAI-compatible API ──

        private sealed class ChatCompletionRequest
        {
            [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
            [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
            [JsonPropertyName("temperature")] public double Temperature { get; set; }
            [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
            [JsonPropertyName("stream")] public bool Stream { get; set; }
        }

        private sealed class ChatMessage
        {
            public ChatMessage() { }
            public ChatMessage(string role, string content) { Role = role; Content = content; }
            [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
            [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
        }

        private sealed class ChatCompletionResponse
        {
            [JsonPropertyName("model")] public string? Model { get; set; }
            [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
        }

        private sealed class Choice
        {
            [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
        }

        // ── Streaming (Server-Sent Events) chunk shapes ──

        private sealed class StreamCompletionChunk
        {
            [JsonPropertyName("choices")] public List<StreamChoice>? Choices { get; set; }
        }

        private sealed class StreamChoice
        {
            [JsonPropertyName("delta")] public StreamDelta? Delta { get; set; }
        }

        private sealed class StreamDelta
        {
            [JsonPropertyName("content")] public string? Content { get; set; }
        }
    }
}
