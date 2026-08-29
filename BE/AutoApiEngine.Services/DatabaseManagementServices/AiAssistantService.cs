using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// AI assistant for the Query Studio. Uses a local Ollama server (or any OpenAI-compatible
    /// API) with MCP-style tool calling to let the AI dynamically discover database schema
    /// and inspect data instead of pre-loading all schema context into the prompt.
    ///
    /// System instructions are loaded from the backend prompt file
    /// <c>Prompts/db-copilot-instructions.md</c> at startup and cached. Edit that file to
    /// change the assistant's behavior; it is deployed next to the application.
    ///
    /// Supports DDL operations (CREATE, ALTER, DROP, INSERT, UPDATE, DELETE) with
    /// user confirmation via the execute_write tool.
    /// </summary>
    public class AiAssistantService : IAiAssistantService
    {
        private readonly HttpClient _httpClient;
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IDatabaseToolService _databaseTools;
        private readonly OpenRouterAiSettings _settings;
        private readonly ILogger<AiAssistantService> _logger;
        private readonly IConfiguration _configuration;

        // Max rounds of tool calls to prevent infinite loops
        private const int MaxToolRounds = 5;

        // Cached system instructions loaded from Prompts/db-copilot-instructions.md
        private static string? _cachedInstructions;
        private static readonly object InstructionsLock = new();

        public AiAssistantService(
            HttpClient httpClient,
            IWorkspaceRepository workspaceRepository,
            IDatabaseToolService databaseTools,
            IOptions<OpenRouterAiSettings> settings,
            IConfiguration configuration,
            ILogger<AiAssistantService> logger)
        {
            _httpClient = httpClient;
            _workspaceRepository = workspaceRepository;
            _databaseTools = databaseTools;
            _settings = settings.Value;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<AiAssistResponse> AssistAsync(AiAssistRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                return new AiAssistResponse
                {
                    Success = false,
                    Error = "AI assistant is not configured. Set the OpenRouterAi:BaseUrl configuration value."
                };
            }

            // ── Resolve workspace database info ──
            var (engine, databaseName, connectionString) = await ResolveDatabaseInfoAsync(request.WorkspaceId, cancellationToken);
            if (engine == null)
            {
                return new AiAssistResponse { Success = false, Error = "Workspace not found or has no database configured." };
            }

            // ── Build initial messages with file-based system instructions ──
            var systemPrompt = BuildSystemPromptWithInstructions(engine.Value, databaseName!);
            var messages = AiPromptBuilder.BuildMessages(request, engine.ToString()!, systemPrompt);
            var tools = AiPromptBuilder.BuildToolDefinitions();

            // ── Multi-round tool-calling loop ──
            for (int round = 0; round < MaxToolRounds; round++)
            {
                var payload = new ChatCompletionRequest
                {
                    Model = _settings.Model,
                    Messages = messages,
                    Temperature = _settings.Temperature,
                    MaxTokens = _settings.MaxTokens,
                    Stream = false,
                    Tools = tools,
                    KeepAlive = string.IsNullOrWhiteSpace(_settings.KeepAlive) ? null : _settings.KeepAlive
                };

                ChatCompletionResponse? completion;
                try
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions")
                    {
                        Content = JsonContent.Create(payload, options: AiJsonOptions.Default)
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
                            Error = AiPromptBuilder.FormatProviderError((int)httpResponse.StatusCode, body)
                        };
                    }

                    completion = await httpResponse.Content.ReadFromJsonAsync<ChatCompletionResponse>(AiJsonOptions.Default, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogError(ex, "AI request timed out after {Timeout}s contacting {BaseUrl}.", _httpClient.Timeout.TotalSeconds, _settings.BaseUrl);
                    return new AiAssistResponse
                    {
                        Success = false,
                        Error = $"The AI provider did not respond in time ({_httpClient.Timeout.TotalSeconds:N0}s). The endpoint '{_settings.BaseUrl}' may be unreachable. Verify the AI provider is running and the OpenRouterAi:BaseUrl setting is correct."
                    };
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Network error reaching AI provider at {BaseUrl}.", _settings.BaseUrl);
                    return new AiAssistResponse
                    {
                        Success = false,
                        Error = $"Could not connect to the AI provider at '{_settings.BaseUrl}'. Verify the AI provider is running and the OpenRouterAi:BaseUrl setting is correct."
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AI assistant request failed.");
                    return new AiAssistResponse { Success = false, Error = "The AI service failed unexpectedly. Please try again." };
                }

                var choice = completion?.Choices?.FirstOrDefault();
                if (choice?.Message == null)
                {
                    return new AiAssistResponse { Success = false, Error = "The AI returned an empty response." };
                }

                // Add assistant message to conversation
                messages.Add(choice.Message);

                // Check for tool calls
                if (choice.Message.ToolCalls is { Count: > 0 })
                {
                    foreach (var toolCall in choice.Message.ToolCalls)
                    {
                        var result = await ExecuteToolCallAsync(toolCall, databaseName!, engine!.Value, connectionString!, cancellationToken);
                        messages.Add(new ChatMessage("tool", result, toolCall.Id));
                    }
                    // Continue loop for next round
                    continue;
                }

                // JSON fallback: model emitted a tool call as text instead of tool_calls.
                var fallback = await TryExecuteJsonFallbackToolCallAsync(
                    choice.Message.Content, databaseName!, engine!.Value, connectionString!, cancellationToken);
                if (fallback.Handled)
                {
                    messages.Add(new ChatMessage("user", $"[Tool result: {fallback.ToolName}]\n{fallback.Result}"));
                    continue;
                }

                // No tool calls — this is the final text response
                var reply = choice.Message.Content?.Trim();
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
            if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                yield return new AiStreamChunk { Error = "AI assistant is not configured. Set the OpenRouterAi:BaseUrl configuration value." };
                yield break;
            }

            // ── Resolve workspace database info ──
            var (engine, databaseName, connectionString) = await ResolveDatabaseInfoAsync(request.WorkspaceId, cancellationToken);
            if (engine == null)
            {
                yield return new AiStreamChunk { Error = "Workspace not found or has no database configured." };
                yield break;
            }

            // ── Process tool calls first (non-streaming) ──
            var systemPrompt = BuildSystemPromptWithInstructions(engine.Value, databaseName!);
            var messages = AiPromptBuilder.BuildMessages(request, engine.ToString()!, systemPrompt);
            var tools = AiPromptBuilder.BuildToolDefinitions();

            var toolResult = await ProcessToolRoundsForStreamAsync(messages, tools, databaseName!, engine!.Value, connectionString!, cancellationToken);

            if (toolResult.Cancelled) yield break;

            if (toolResult.Error != null)
            {
                yield return new AiStreamChunk { Error = toolResult.Error };
                yield break;
            }

            // Tool rounds complete — messages now contains the full tool-call history.
            // Stream the final AI response token-by-token.
            await foreach (var chunk in StreamFinalResponseAsync(messages, toolResult.DbChanged, cancellationToken))
            {
                yield return chunk;
            }
        }

        /// <summary>
        /// Makes a single streaming (SSE) call to the AI provider and yields token chunks.
        /// Called after all tool-call rounds are complete so that the final text response
        /// is streamed in real-time rather than buffered into a single chunk.
        /// </summary>
        private async IAsyncEnumerable<AiStreamChunk> StreamFinalResponseAsync(
            List<ChatMessage> messages,
            bool dbChanged,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var payload = new ChatCompletionRequest
            {
                Model = _settings.Model,
                Messages = messages,
                Temperature = _settings.Temperature,
                MaxTokens = _settings.MaxTokens,
                Stream = true,
                KeepAlive = string.IsNullOrWhiteSpace(_settings.KeepAlive) ? null : _settings.KeepAlive
            };

            // Phase 1: Open the HTTP connection (no yield — catch is allowed here).
            HttpResponseMessage? httpResponse = null;
            string? connectionError = null;
            bool cancelled = false;

            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions")
                {
                    Content = JsonContent.Create(payload, options: AiJsonOptions.Default)
                };
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

                httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                    connectionError = AiPromptBuilder.FormatProviderError((int)httpResponse.StatusCode, body);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not start AI stream at {BaseUrl}.", _settings.BaseUrl);
                connectionError = $"Could not connect to the AI provider at '{_settings.BaseUrl}'.";
            }

            // Phase 2: Yield error / cancellation — safe outside try-catch.
            if (cancelled)
            {
                httpResponse?.Dispose();
                yield break;
            }
            if (connectionError != null)
            {
                httpResponse?.Dispose();
                yield return new AiStreamChunk { Error = connectionError };
                yield break;
            }

            // Phase 3: Read and yield SSE chunks — try-finally is allowed with yield.
            Stream? responseStream = null;
            try
            {
                responseStream = await httpResponse!.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new System.IO.StreamReader(responseStream);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line == null) break;
                    if (!line.StartsWith("data:")) continue;

                    var data = line["data:".Length..].Trim();
                    if (data == "[DONE]") break;
                    if (string.IsNullOrEmpty(data)) continue;

                    StreamCompletionChunk? sseChunk;
                    try { sseChunk = System.Text.Json.JsonSerializer.Deserialize<StreamCompletionChunk>(data, AiJsonOptions.Default); }
                    catch { continue; }

                    var content = sseChunk?.Choices?.FirstOrDefault()?.Delta?.Content;
                    if (!string.IsNullOrEmpty(content))
                        yield return new AiStreamChunk { Delta = content };

                    if (sseChunk?.Choices?.FirstOrDefault()?.FinishReason == "stop") break;
                }

                yield return new AiStreamChunk { Done = true, Model = _settings.Model, DbChanged = dbChanged };
            }
            finally
            {
                responseStream?.Dispose();
                httpResponse?.Dispose();
            }
        }

        // ── System Instructions Loading ──

        /// <summary>
        /// Builds the system prompt by loading instructions from
        /// <c>Prompts/db-copilot-instructions.md</c> and prepending the database context
        /// prefix (engine + database name). Instructions are cached after first load.
        /// </summary>
        private string BuildSystemPromptWithInstructions(DatabaseEngine engine, string databaseName)
        {
            var instructions = GetSystemInstructions();
            var dbContextPrefix = BuildDatabaseContextPrefix(engine, databaseName);
            return $"{dbContextPrefix}\n\n{instructions}";
        }

        /// <summary>
        /// Loads and caches the DB Copilot system instructions from the backend prompt file
        /// <c>Prompts/db-copilot-instructions.md</c> (copied next to the app at build time).
        /// This file is the single source of truth for the assistant's behavior.
        /// </summary>
        private static string GetSystemInstructions()
        {
            if (_cachedInstructions != null) return _cachedInstructions;

            lock (InstructionsLock)
            {
                if (_cachedInstructions != null) return _cachedInstructions;

                var path = ResolveInstructionsPath();
                if (string.IsNullOrEmpty(path))
                {
                    throw new InvalidOperationException(
                        "Could not locate the DB Copilot prompt file 'Prompts/db-copilot-instructions.md'. " +
                        "Ensure it is deployed next to the application (CopyToOutputDirectory=PreserveNewest).");
                }

                _cachedInstructions = File.ReadAllText(path);
                return _cachedInstructions;
            }
        }

        /// <summary>
        /// Locates <c>Prompts/db-copilot-instructions.md</c>: checks the app base directory first
        /// (where it is copied at build time), then walks up the tree to find the source file
        /// under <c>AutoApiEngine.ApiServices/Prompts</c> when running from a dev layout.
        /// Returns an empty string if it cannot be found.
        /// </summary>
        private static string ResolveInstructionsPath()
        {
            const string fileName = "db-copilot-instructions.md";

            var baseCandidate = Path.Combine(AppContext.BaseDirectory, "Prompts", fileName);
            if (File.Exists(baseCandidate)) return baseCandidate;

            var current = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && current != null; i++)
            {
                var candidate = Path.Combine(current, "AutoApiEngine.ApiServices", "Prompts", fileName);
                if (File.Exists(candidate)) return candidate;
                current = Path.GetDirectoryName(current);
            }

            return string.Empty;
        }

        /// <summary>
        /// Builds a short database context prefix prepended to the system prompt.
        /// This tells the AI model which database engine and name it is working with,
        /// so it targets the correct workspace database and uses the right SQL dialect.
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

            return $"[Workspace Database: {engineName} / {databaseName}] — Use the database tools to query this database. All SQL must use {engineName} syntax.";
        }

        // ── Helpers ──

        private async Task<(DatabaseEngine? engine, string? databaseName, string? connectionString)> ResolveDatabaseInfoAsync(
            string workspaceId, CancellationToken cancellationToken)
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

        private async Task<string> ExecuteToolCallAsync(
            ToolCall toolCall, string databaseName, DatabaseEngine engine, string connectionString,
            CancellationToken cancellationToken)
        {
            var fn = toolCall.Function;
            try
            {
                return fn.Name switch
                {
                    "list_tables" => await _databaseTools.ListTablesAsync(databaseName, engine, connectionString, cancellationToken),
                    "describe_table" => await ExecuteDescribeTableAsync(fn, databaseName, engine, connectionString, cancellationToken),
                    "search_schema" => await ExecuteSearchSchemaAsync(fn, databaseName, engine, connectionString, cancellationToken),
                    "list_views" => await _databaseTools.ListViewsAsync(databaseName, engine, connectionString, cancellationToken),
                    "list_routines" => await _databaseTools.ListRoutinesAsync(databaseName, engine, connectionString, cancellationToken),
                    "execute_query" => await ExecuteReadOnlyQueryAsync(fn, databaseName, engine, connectionString, cancellationToken),
                    "execute_write" => await ExecuteWriteAsync(fn, databaseName, engine, connectionString, cancellationToken),
                    _ => $"Error: Unknown tool '{fn.Name}'."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool call '{Name}' failed", fn.Name);
                return $"Error executing '{fn.Name}': {ex.Message}";
            }
        }

        /// <summary>
        /// Handles the case where a (typically small) model emits a tool call as plain text /
        /// JSON — e.g. <c>{"tool":"execute_query","arguments":{...}}</c> — instead of a native
        /// <c>tool_calls</c> array. Parses the JSON fallback documented in the system prompt,
        /// executes the tool, and reports the result so the tool loop can continue.
        /// Returns <c>Handled = false</c> when the content is not a recognizable tool call.
        /// </summary>
        private async Task<(bool Handled, string ToolName, string Result, bool DbChanged)> TryExecuteJsonFallbackToolCallAsync(
            string? content, string databaseName, DatabaseEngine engine, string connectionString,
            CancellationToken cancellationToken)
        {
            if (!AiPromptBuilder.TryParseJsonToolCall(content, out var toolName, out var argumentsJson))
                return (false, string.Empty, string.Empty, false);

            var syntheticCall = new ToolCall
            {
                Id = $"call_{Guid.NewGuid():N}",
                Function = new FunctionCall { Name = toolName, Arguments = argumentsJson }
            };

            var result = await ExecuteToolCallAsync(syntheticCall, databaseName, engine, connectionString, cancellationToken);
            var dbChanged = toolName.Equals("execute_write", StringComparison.OrdinalIgnoreCase)
                            && result.StartsWith("Success:", StringComparison.OrdinalIgnoreCase);
            return (true, toolName, result, dbChanged);
        }

        private async Task<string> ExecuteWriteAsync(
            FunctionCall fn, string databaseName, DatabaseEngine engine, string connectionString,
            CancellationToken cancellationToken)
        {
            var args = JsonSerializer.Deserialize<JsonElement>(fn.Arguments, AiJsonOptions.Default);
            var sql = args.GetProperty("sql").GetString();
            return string.IsNullOrWhiteSpace(sql)
                ? "Error: Missing 'sql' argument."
                : await _databaseTools.ExecuteWriteAsync(databaseName, engine, connectionString, sql, cancellationToken);
        }

        private async Task<string> ExecuteDescribeTableAsync(
            FunctionCall fn, string databaseName, DatabaseEngine engine, string connectionString,
            CancellationToken cancellationToken)
        {
            var args = JsonSerializer.Deserialize<JsonElement>(fn.Arguments, AiJsonOptions.Default);
            var tableName = args.GetProperty("table_name").GetString();
            return string.IsNullOrWhiteSpace(tableName)
                ? "Error: Missing 'table_name' argument."
                : await _databaseTools.DescribeTableAsync(databaseName, engine, connectionString, tableName, cancellationToken);
        }

        private async Task<string> ExecuteSearchSchemaAsync(
            FunctionCall fn, string databaseName, DatabaseEngine engine, string connectionString,
            CancellationToken cancellationToken)
        {
            var args = JsonSerializer.Deserialize<JsonElement>(fn.Arguments, AiJsonOptions.Default);
            var query = args.GetProperty("query").GetString();
            return string.IsNullOrWhiteSpace(query)
                ? "Error: Missing 'query' argument."
                : await _databaseTools.SearchSchemaAsync(databaseName, engine, connectionString, query, cancellationToken);
        }

        private async Task<string> ExecuteReadOnlyQueryAsync(
            FunctionCall fn, string databaseName, DatabaseEngine engine, string connectionString,
            CancellationToken cancellationToken)
        {
            var args = JsonSerializer.Deserialize<JsonElement>(fn.Arguments, AiJsonOptions.Default);
            var sql = args.GetProperty("sql").GetString();
            return string.IsNullOrWhiteSpace(sql)
                ? "Error: Missing 'sql' argument."
                : await _databaseTools.ExecuteReadOnlyQueryAsync(databaseName, engine, connectionString, sql, cancellationToken);
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

        /// <summary>
        /// Runs tool-calling rounds without yielding, so the caller (StreamAsync) can
        /// safely yield results outside any try-catch block.
        /// </summary>
        private async Task<ToolRoundResult> ProcessToolRoundsAsync(
            List<ChatMessage> messages, List<ToolDefinition> tools,
            string databaseName, DatabaseEngine engine, string connectionString,
            CancellationToken cancellationToken)
        {
            for (int round = 0; round < MaxToolRounds; round++)
            {
                var payload = new ChatCompletionRequest
                {
                    Model = _settings.Model,
                    Messages = messages,
                    Temperature = _settings.Temperature,
                    MaxTokens = _settings.MaxTokens,
                    Stream = false,
                    Tools = tools,
                    KeepAlive = string.IsNullOrWhiteSpace(_settings.KeepAlive) ? null : _settings.KeepAlive
                };

                ChatCompletionResponse? completion;
                try
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions")
                    {
                        Content = JsonContent.Create(payload, options: AiJsonOptions.Default)
                    };
                    httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

                    using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("AI provider returned {Status}: {Body}", (int)httpResponse.StatusCode, body);
                        return new ToolRoundResult { Error = AiPromptBuilder.FormatProviderError((int)httpResponse.StatusCode, body) };
                    }

                    completion = await httpResponse.Content.ReadFromJsonAsync<ChatCompletionResponse>(AiJsonOptions.Default, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return new ToolRoundResult { Cancelled = true };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not reach AI provider at {BaseUrl}.", _settings.BaseUrl);
                    return new ToolRoundResult { Error = $"Could not connect to the AI provider at '{_settings.BaseUrl}'." };
                }

                var choice = completion?.Choices?.FirstOrDefault();
                if (choice?.Message == null)
                {
                    return new ToolRoundResult { Error = "The AI returned an empty response." };
                }

                messages.Add(choice.Message);

                if (choice.Message.ToolCalls is { Count: > 0 })
                {
                    foreach (var toolCall in choice.Message.ToolCalls)
                    {
                        var result = await ExecuteToolCallAsync(toolCall, databaseName, engine, connectionString, cancellationToken);
                        messages.Add(new ChatMessage("tool", result, toolCall.Id));
                    }
                    continue;
                }

                // JSON fallback: model emitted a tool call as text instead of tool_calls.
                var fallback = await TryExecuteJsonFallbackToolCallAsync(
                    choice.Message.Content, databaseName, engine, connectionString, cancellationToken);
                if (fallback.Handled)
                {
                    messages.Add(new ChatMessage("user", $"[Tool result: {fallback.ToolName}]\n{fallback.Result}"));
                    continue;
                }

                // Final answer with tool calls — return here for AssistAsync (which doesn't stream).
                return new ToolRoundResult { FinalMessage = choice.Message };
            }

            return new ToolRoundResult { Error = $"The AI did not produce a final answer after {MaxToolRounds} rounds of tool calls." };
        }

        /// <summary>
        /// Runs tool-call rounds for <see cref="StreamAsync"/>: identical to the main loop
        /// but stops BEFORE making the final non-tool-call API call, leaving <paramref name="messages"/>
        /// in the correct state for a follow-up streaming request.
        /// Returns <see cref="ToolRoundResult.ToolRoundsDone"/> = true when ready to stream.
        /// </summary>
        private async Task<ToolRoundResult> ProcessToolRoundsForStreamAsync(
            List<ChatMessage> messages, List<ToolDefinition> tools,
            string databaseName, DatabaseEngine engine, string connectionString,
            CancellationToken cancellationToken)
        {
            bool dbChanged = false;
            for (int round = 0; round < MaxToolRounds; round++)
            {
                var payload = new ChatCompletionRequest
                {
                    Model = _settings.Model,
                    Messages = messages,
                    Temperature = _settings.Temperature,
                    MaxTokens = _settings.MaxTokens,
                    Stream = false,
                    Tools = tools,
                    KeepAlive = string.IsNullOrWhiteSpace(_settings.KeepAlive) ? null : _settings.KeepAlive
                };

                ChatCompletionResponse? completion;
                try
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions")
                    {
                        Content = JsonContent.Create(payload, options: AiJsonOptions.Default)
                    };
                    httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

                    using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("AI provider returned {Status}: {Body}", (int)httpResponse.StatusCode, body);
                        return new ToolRoundResult { Error = AiPromptBuilder.FormatProviderError((int)httpResponse.StatusCode, body) };
                    }

                    completion = await httpResponse.Content.ReadFromJsonAsync<ChatCompletionResponse>(AiJsonOptions.Default, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return new ToolRoundResult { Cancelled = true };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not reach AI provider at {BaseUrl}.", _settings.BaseUrl);
                    return new ToolRoundResult { Error = $"Could not connect to the AI provider at '{_settings.BaseUrl}'." };
                }

                var choice = completion?.Choices?.FirstOrDefault();
                if (choice?.Message == null)
                    return new ToolRoundResult { Error = "The AI returned an empty response." };

                if (choice.Message.ToolCalls is { Count: > 0 })
                {
                    // Add tool-call message + results; continue to next round.
                    messages.Add(choice.Message);
                    foreach (var toolCall in choice.Message.ToolCalls)
                    {
                        var result = await ExecuteToolCallAsync(toolCall, databaseName, engine, connectionString, cancellationToken);
                        messages.Add(new ChatMessage("tool", result, toolCall.Id));

                        if (toolCall.Function.Name == "execute_write" &&
                            result.StartsWith("Success:", StringComparison.OrdinalIgnoreCase))
                        {
                            dbChanged = true;
                        }
                    }
                    continue;
                }

                // JSON fallback: model emitted a tool call as text instead of tool_calls.
                var fallback = await TryExecuteJsonFallbackToolCallAsync(
                    choice.Message.Content, databaseName, engine, connectionString, cancellationToken);
                if (fallback.Handled)
                {
                    messages.Add(new ChatMessage("assistant", choice.Message.Content ?? string.Empty));
                    messages.Add(new ChatMessage("user", $"[Tool result: {fallback.ToolName}]\n{fallback.Result}"));
                    if (fallback.DbChanged) dbChanged = true;
                    continue;
                }

                // No tool calls → ready for streaming final response.
                // Do NOT add this message to `messages`; the streaming call will generate it.
                return new ToolRoundResult { ToolRoundsDone = true, DbChanged = dbChanged };
            }

            return new ToolRoundResult { Error = $"The AI did not produce a final answer after {MaxToolRounds} rounds of tool calls." };
        }

        /// <summary>
        /// Result of the tool-calling rounds, used to pass data from the non-yielding
        /// helper back to the yielding StreamAsync method.
        /// </summary>
        private sealed class ToolRoundResult
        {
            public ChatMessage? FinalMessage { get; set; }
            public string? Error { get; set; }
            public bool Cancelled { get; set; }
            /// <summary>Set by <see cref="ProcessToolRoundsForStreamAsync"/> when all tool
            /// rounds are complete and the caller should make a streaming final call.</summary>
            public bool ToolRoundsDone { get; set; }
            /// <summary>True when at least one execute_write call succeeded during tool rounds.</summary>
            public bool DbChanged { get; set; }
        }
    }
}
