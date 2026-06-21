using System.Net.Http.Json;
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
    /// AI assistant implementation that calls a local OpenAI-compatible Chat Completions
    /// endpoint (e.g. Ollama, LocalAI, or the Opencode inference server).
    ///
    /// Uses MCP-style tool calling (OpenAI function-calling) to let the AI dynamically
    /// discover database schema and inspect data instead of pre-loading all schema context.
    /// The <see cref="DatabaseToolService"/> enforces read-only SELECT queries.
    ///
    /// This is an alternative to <see cref="AiAssistantService"/> and uses its own
    /// configuration section ("OpencodeAi"). The default model is "gemma4:latest".
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

        public async Task<AiAssistResponse> AssistAsync(AiAssistRequest request, CancellationToken cancellationToken = default)
        {
            // ── Resolve workspace database info ──
            var (engine, databaseName, connectionString) = await ResolveDatabaseInfoAsync(request.WorkspaceId, cancellationToken);
            if (engine == null)
            {
                return new AiAssistResponse { Success = false, Error = "Workspace not found or has no database configured." };
            }

            // ── Build initial messages (no schema context pre-loaded) ──
            var messages = AiPromptBuilder.BuildMessages(request, engine.ToString()!);
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
                    Tools = tools
                };

                ChatCompletionResponse? completion;
                try
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions")
                    {
                        Content = JsonContent.Create(payload, options: AiJsonOptions.Default)
                    };

                    if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                    {
                        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);
                    }

                    using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("Opencode AI provider returned {Status}: {Body}", (int)httpResponse.StatusCode, body);
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
                    _logger.LogError(ex, "Opencode AI request timed out after {Timeout}s contacting {BaseUrl}.", _httpClient.Timeout.TotalSeconds, _settings.BaseUrl);
                    return new AiAssistResponse
                    {
                        Success = false,
                        Error = $"The local AI provider did not respond in time ({_httpClient.Timeout.TotalSeconds:N0}s). The endpoint '{_settings.BaseUrl}' may not be running. Verify OpencodeAi:BaseUrl."
                    };
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Network error reaching Opencode AI provider at {BaseUrl}.", _settings.BaseUrl);
                    return new AiAssistResponse
                    {
                        Success = false,
                        Error = $"Could not connect to the local AI provider at '{_settings.BaseUrl}'. Make sure the server is running (e.g. `ollama serve`) and that OpencodeAi:BaseUrl is correct."
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Opencode AI assistant request failed.");
                    return new AiAssistResponse { Success = false, Error = "The local AI service failed unexpectedly. Please try again." };
                }

                var choice = completion?.Choices?.FirstOrDefault();
                if (choice?.Message == null)
                {
                    return new AiAssistResponse { Success = false, Error = "The AI returned an empty response." };
                }

                messages.Add(choice.Message);

                if (choice.Message.ToolCalls is { Count: > 0 })
                {
                    foreach (var toolCall in choice.Message.ToolCalls)
                    {
                        var result = await ExecuteToolCallAsync(toolCall, databaseName!, engine!.Value, connectionString!, cancellationToken);
                        messages.Add(new ChatMessage("tool", result, toolCall.Id));
                    }
                    continue;
                }

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
            // ── Resolve workspace database info ──
            var (engine, databaseName, connectionString) = await ResolveDatabaseInfoAsync(request.WorkspaceId, cancellationToken);
            if (engine == null)
            {
                yield return new AiStreamChunk { Error = "Workspace not found or has no database configured." };
                yield break;
            }

            // ── Build initial messages ──
            var messages = AiPromptBuilder.BuildMessages(request, engine.ToString()!);
            var tools = AiPromptBuilder.BuildToolDefinitions();

            // ── Process tool calls first (non-streaming) ──
            var toolResult = await ProcessToolRoundsAsync(messages, tools, databaseName!, engine!.Value, connectionString!, cancellationToken);

            if (toolResult.Error != null)
            {
                yield return new AiStreamChunk { Error = toolResult.Error };
                yield break;
            }

            if (toolResult.FinalMessage == null)
            {
                yield return new AiStreamChunk { Error = $"The AI did not produce a final answer after {MaxToolRounds} rounds of tool calls." };
                yield break;
            }

            var text = toolResult.FinalMessage.Content;
            if (!string.IsNullOrEmpty(text))
            {
                yield return new AiStreamChunk { Delta = text };
            }
            yield return new AiStreamChunk { Done = true, Model = _settings.Model, DbChanged = toolResult.DbChanged };
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
                    Tools = tools
                };

                ChatCompletionResponse? completion;
                try
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions")
                    {
                        Content = JsonContent.Create(payload, options: AiJsonOptions.Default)
                    };

                    if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                    {
                        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);
                    }

                    using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("Opencode AI provider returned {Status}: {Body}", (int)httpResponse.StatusCode, body);
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
                    _logger.LogError(ex, "Could not start Opencode AI stream at {BaseUrl}.", _settings.BaseUrl);
                    return new ToolRoundResult { Error = $"Could not connect to the local AI provider at '{_settings.BaseUrl}'." };
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

                        if (toolCall.Function.Name == "execute_write" &&
                            result.StartsWith("Success:", StringComparison.OrdinalIgnoreCase))
                        {
                            dbChanged = true;
                        }
                    }
                    continue;
                }

                return new ToolRoundResult { FinalMessage = choice.Message, DbChanged = dbChanged };
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
            public bool DbChanged { get; set; }
        }
    }
}
