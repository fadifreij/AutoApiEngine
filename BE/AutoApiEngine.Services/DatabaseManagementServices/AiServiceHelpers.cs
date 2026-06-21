using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Logging;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    // ── Shared serialization types for the OpenAI-compatible Chat Completions API ──

    /// <summary>
    /// Request body for POST /v1/chat/completions (OpenAI-compatible).
    /// Supports both regular messages and tool/function calling.
    /// </summary>
    public sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("tools")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ToolDefinition>? Tools { get; set; }
    }

    /// <summary>
    /// A single message in the chat completion conversation.
    /// Supports regular roles (system/user/assistant) plus tool_call_id for tool results.
    /// </summary>
    public sealed class ChatMessage
    {
        public ChatMessage() { }
        public ChatMessage(string role, string content) { Role = role; Content = content; }
        public ChatMessage(string role, string content, string toolCallId)
        {
            Role = role; Content = content; ToolCallId = toolCallId;
        }

        [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string? Content { get; set; }

        [JsonPropertyName("tool_call_id")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; set; }

        [JsonPropertyName("tool_calls")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ToolCall>? ToolCalls { get; set; }
    }

    // ── Tool / Function Calling types ──

    /// <summary>
    /// A tool definition that the AI model can call (OpenAI "function" tool type).
    /// </summary>
    public sealed class ToolDefinition
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public FunctionDefinition Function { get; set; } = new();
    }

    /// <summary>
    /// The function descriptor inside a tool definition.
    /// </summary>
    public sealed class FunctionDefinition
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("parameters")] public JsonElement Parameters { get; set; }
    }

    /// <summary>
    /// A tool call returned by the AI model in a response.
    /// </summary>
    public sealed class ToolCall
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("type")] public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public FunctionCall Function { get; set; } = new();
    }

    /// <summary>
    /// The function call details inside a tool call.
    /// </summary>
    public sealed class FunctionCall
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("arguments")] public string Arguments { get; set; } = "{}";
    }

    // ── Response types ──

    /// <summary>
    /// Response body from a non-streaming POST /v1/chat/completions.
    /// </summary>
    public sealed class ChatCompletionResponse
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    /// <summary>
    /// A single choice in the completion response.
    /// </summary>
    public sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    // ── Streaming (Server-Sent Events) chunk shapes ──

    /// <summary>
    /// A single SSE chunk from a streaming POST /v1/chat/completions.
    /// </summary>
    public sealed class StreamCompletionChunk
    {
        [JsonPropertyName("choices")] public List<StreamChoice>? Choices { get; set; }
    }

    /// <summary>
    /// A single streaming choice containing a delta.
    /// </summary>
    public sealed class StreamChoice
    {
        [JsonPropertyName("delta")] public StreamDelta? Delta { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    /// <summary>
    /// The incremental content delta within a streaming chunk.
    /// </summary>
    public sealed class StreamDelta
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("tool_calls")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ToolCall>? ToolCalls { get; set; }
    }

    // ── Shared JSON serializer options ──

    public static class AiJsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    // ── Helpers ──

    /// <summary>
    /// Shared helper methods for building system prompts, tool definitions,
    /// and error messages used by all IAiAssistantService implementations.
    /// </summary>
    public static class AiPromptBuilder
    {
        /// <summary>
        /// Builds the system prompt that defines the DB Copilot persona.
        /// No schema context is pre-loaded — the AI uses MCP tools to discover schema on demand.
        /// </summary>
        public static string BuildSystemPrompt(string engineName)
        {
            return
$@"You are ""DB Copilot"", an expert database assistant embedded in a SQL Query Studio.
You help the user ONLY with topics related to their current database ({engineName}):
- Writing and refining DDL (CREATE/ALTER TABLE, indexes, constraints, views, stored procedures).
- Writing, explaining, and OPTIMIZING SELECT queries, joins, and stored procedures/functions.
- Executing DML and DDL statements inside the current database after user confirmation.
- Performance tuning: indexing strategy, query plans, sargability, normalization, statistics.

You have access to database tools that let you discover the schema and inspect data.
ALWAYS use them to look up real table and column information — never guess or invent names.

STRICT RULES:
1. Stay on topic. If asked anything unrelated to this database, SQL, or database performance, politely decline and steer back to database help.
2. SCOPE: You are connected to the current workspace database ONLY. Never use USE statements to switch databases, and never reference other databases. All operations target objects within this database exclusively.
3. Tailor all SQL to the {engineName} dialect.
4. MANDATORY BEFORE ANY DML: Before writing INSERT, UPDATE, or DELETE, you MUST first call describe_table to get the exact column names, data types, nullability, and primary key. Never invent column names.

== EXECUTION WORKFLOW ==

FOLLOW THIS WORKFLOW FOR EVERY REQUEST:

A) SELECT queries / read-only inspection:
   - Call execute_query immediately and display the results to the user.
   - No confirmation needed.

B) DDL statements (CREATE TABLE, ALTER TABLE, DROP TABLE, TRUNCATE, CREATE INDEX, CREATE VIEW, CREATE PROCEDURE, etc.):
   1. Discover the current schema using list_tables / describe_table as needed.
   2. Generate the SQL and present it in a ```sql code block with a brief explanation.
   3. Ask the user: ""Shall I execute this on the database? Reply yes to confirm or no to cancel.""
   4. STOP and wait. Do NOT call execute_write until the user explicitly confirms with yes, execute, go ahead, confirm, or equivalent.
   5. On confirmation → call execute_write and report the result.
   6. Then call execute_query (e.g. SHOW COLUMNS / SELECT) to show the updated state.

C) DML statements (INSERT, UPDATE, DELETE):
   1. Call describe_table to get the real schema.
   2. Generate the exact SQL and present it in a ```sql code block.
   3. Ask the user: ""Shall I execute this on the database? Reply yes to confirm or no to cancel.""
   4. STOP and wait. Do NOT call execute_write until the user explicitly confirms.
   5. On confirmation → call execute_write and report the result.
   6. Then call execute_query to confirm the change is visible in the data.

D) Stored procedure / function execution (EXEC / CALL):
   1. Confirm the procedure exists using list_routines.
   2. Show the EXEC / CALL statement you plan to run.
   3. Ask the user for confirmation before running.
   4. On confirmation → call execute_write.

== ADDITIONAL SAFETY RULES ==
5. For DELETE, ALWAYS include a WHERE clause. Never delete all rows. Add a ⚠️ note explaining which rows will be affected.
6. For DROP TABLE or TRUNCATE, add a ⚠️ warning about irreversibility before asking for confirmation.
7. For any operation that affects the database container itself (DROP DATABASE, CREATE DATABASE, ALTER DATABASE), refuse and explain it is not permitted.
8. Prefer the smallest, safest change that solves the problem. Add brief comments to non-trivial SQL.
9. Keep answers concise. Use markdown with ```sql code fences for all SQL.";
        }

        /// <summary>
        /// Returns tool definitions for database MCP tools that the AI can call.
        /// These are OpenAI-compatible function-calling tool definitions scoped to the
        /// workspace's database.
        /// </summary>
        public static List<ToolDefinition> BuildToolDefinitions()
        {
            return new List<ToolDefinition>
            {
                new()
                {
                    Function = new FunctionDefinition
                    {
                        Name = "list_tables",
                        Description = "List all table names in the current database",
                        Parameters = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""")
                    }
                },
                new()
                {
                    Function = new FunctionDefinition
                    {
                        Name = "describe_table",
                        Description = "Show columns, types, primary keys, and nullability for a specific table",
                        Parameters = JsonSerializer.Deserialize<JsonElement>(
                            """{"type":"object","properties":{"table_name":{"type":"string","description":"Name of the table to describe"}},"required":["table_name"]}""")
                    }
                },
                new()
                {
                    Function = new FunctionDefinition
                    {
                        Name = "search_schema",
                        Description = "Search for tables, views, or columns matching a keyword",
                        Parameters = JsonSerializer.Deserialize<JsonElement>(
                            """{"type":"object","properties":{"query":{"type":"string","description":"Keyword to search for in table/column names"}},"required":["query"]}""")
                    }
                },
                new()
                {
                    Function = new FunctionDefinition
                    {
                        Name = "list_views",
                        Description = "List all views with their SQL definitions",
                        Parameters = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""")
                    }
                },
                new()
                {
                    Function = new FunctionDefinition
                    {
                        Name = "list_routines",
                        Description = "List all stored procedures and functions with their SQL definitions",
                        Parameters = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""")
                    }
                },
                new()
                {
                    Function = new FunctionDefinition
                    {
                        Name = "execute_query",
                        Description =
                            "Execute a read-only SELECT query to inspect data. Returns up to 100 rows as a markdown table. ONLY SELECT queries are allowed.",
                        Parameters = JsonSerializer.Deserialize<JsonElement>(
                            """{"type":"object","properties":{"sql":{"type":"string","description":"SELECT SQL to execute"}},"required":["sql"]}""")
                    }
                },
                new()
                {
                    Function = new FunctionDefinition
                    {
                        Name = "execute_write",
                        Description =
                            "Execute any SQL statement on objects inside the current workspace database: INSERT, UPDATE, DELETE, CREATE TABLE, ALTER TABLE, DROP TABLE, TRUNCATE, CREATE INDEX, CREATE VIEW, CREATE PROCEDURE, EXEC/CALL (local stored procedures). Database-level ops (DROP DATABASE, CREATE DATABASE), USE, GRANT/REVOKE, and xp_ system procs are blocked. Always call describe_table before DML, and always get user confirmation before calling this tool for DDL or DML.",
                        Parameters = JsonSerializer.Deserialize<JsonElement>(
                            """{"type":"object","properties":{"sql":{"type":"string","description":"SQL statement to execute on the current database"}},"required":["sql"]}""")
                    }
                }
            };
        }

        /// <summary>
        /// Builds the message list for the AI request from the user's input.
        /// Unlike the old approach, this does NOT pre-load schema context.
        /// Schema discovery happens on-demand via MCP tool calls.
        /// </summary>
        public static List<ChatMessage> BuildMessages(
            AiAssistRequest request,
            string engineName)
        {
            var messages = new List<ChatMessage>
            {
                new("system", BuildSystemPrompt(engineName))
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

        /// <summary>
        /// Builds a user-facing error message from a failed provider response.
        /// </summary>
        public static string FormatProviderError(int status, string? body)
        {
            var detail = ExtractProviderMessage(body);
            return string.IsNullOrWhiteSpace(detail)
                ? $"AI provider error ({status}). Please try again."
                : $"AI provider error ({status}): {detail}";
        }

        /// <summary>
        /// Extracts the error message from a provider error response body.
        /// </summary>
        public static string? ExtractProviderMessage(string? body)
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
    }
}
