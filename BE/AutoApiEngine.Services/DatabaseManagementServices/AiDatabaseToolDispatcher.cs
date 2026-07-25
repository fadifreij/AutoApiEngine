using System.Text.Json;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Shared dispatcher that executes a named database tool (list_tables, describe_table,
    /// search_schema, list_views, list_routines, execute_query, execute_write) against the
    /// current workspace database.
    ///
    /// Used by the <see cref="AiAssistantService"/> to execute tool calls from the AI model
    /// using the JSON tool-call-in-text protocol or OpenAI function-calling.
    /// </summary>
    public static class AiDatabaseToolDispatcher
    {
        public static async Task<string> ExecuteAsync(
            IDatabaseToolService databaseTools,
            string toolName,
            JsonElement args,
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            CancellationToken ct)
        {
            try
            {
                return toolName switch
                {
                    "list_tables" => await databaseTools.ListTablesAsync(databaseName, engine, connectionString, ct),
                    "describe_table" => await DescribeTableAsync(databaseTools, args, databaseName, engine, connectionString, ct),
                    "search_schema" => await SearchSchemaAsync(databaseTools, args, databaseName, engine, connectionString, ct),
                    "list_views" => await databaseTools.ListViewsAsync(databaseName, engine, connectionString, ct),
                    "list_routines" => await databaseTools.ListRoutinesAsync(databaseName, engine, connectionString, ct),
                    "execute_query" => await ExecuteReadOnlyQueryAsync(databaseTools, args, databaseName, engine, connectionString, ct),
                    "execute_write" => await ExecuteWriteAsync(databaseTools, args, databaseName, engine, connectionString, ct),
                    _ => $"Error: Unknown tool '{toolName}'."
                };
            }
            catch (Exception ex)
            {
                return $"Error executing '{toolName}': {ex.Message}";
            }
        }

        private static async Task<string> ExecuteWriteAsync(
            IDatabaseToolService databaseTools, JsonElement args, string databaseName, DatabaseEngine engine,
            string connectionString, CancellationToken ct)
        {
            var sql = GetStringProperty(args, "sql");
            return string.IsNullOrWhiteSpace(sql)
                ? "Error: Missing 'sql' argument."
                : await databaseTools.ExecuteWriteAsync(databaseName, engine, connectionString, sql, ct);
        }

        private static async Task<string> DescribeTableAsync(
            IDatabaseToolService databaseTools, JsonElement args, string databaseName, DatabaseEngine engine,
            string connectionString, CancellationToken ct)
        {
            var tableName = GetStringProperty(args, "table_name");
            return string.IsNullOrWhiteSpace(tableName)
                ? "Error: Missing 'table_name' argument."
                : await databaseTools.DescribeTableAsync(databaseName, engine, connectionString, tableName, ct);
        }

        private static async Task<string> SearchSchemaAsync(
            IDatabaseToolService databaseTools, JsonElement args, string databaseName, DatabaseEngine engine,
            string connectionString, CancellationToken ct)
        {
            var query = GetStringProperty(args, "query");
            return string.IsNullOrWhiteSpace(query)
                ? "Error: Missing 'query' argument."
                : await databaseTools.SearchSchemaAsync(databaseName, engine, connectionString, query, ct);
        }

        private static async Task<string> ExecuteReadOnlyQueryAsync(
            IDatabaseToolService databaseTools, JsonElement args, string databaseName, DatabaseEngine engine,
            string connectionString, CancellationToken ct)
        {
            var sql = GetStringProperty(args, "sql");
            return string.IsNullOrWhiteSpace(sql)
                ? "Error: Missing 'sql' argument."
                : await databaseTools.ExecuteReadOnlyQueryAsync(databaseName, engine, connectionString, sql, ct);
        }

        private static string GetStringProperty(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(propertyName, out var prop) &&
                   prop.ValueKind == JsonValueKind.String
                ? prop.GetString() ?? ""
                : "";
        }
    }
}
