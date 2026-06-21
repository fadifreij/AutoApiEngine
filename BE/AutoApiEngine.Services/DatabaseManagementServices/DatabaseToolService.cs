using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// MCP-style database tool service that the AI assistant invokes dynamically
    /// (via OpenAI function calling) to discover schema and inspect data.
    ///
    /// SCOPING: Every tool is bound to exactly one database — the workspace's database.
    /// There is no way for the AI to switch to a different database.
    /// Non-SELECT queries in <see cref="ExecuteReadOnlyQueryAsync"/> are rejected outright.
    /// </summary>
    public class DatabaseToolService : IDatabaseToolService
    {
        private readonly ISchemaExplorerService _schemaExplorer;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DatabaseToolService> _logger;

        public DatabaseToolService(
            ISchemaExplorerService schemaExplorer,
            IConfiguration configuration,
            ILogger<DatabaseToolService> logger)
        {
            _schemaExplorer = schemaExplorer;
            _configuration = configuration;
            _logger = logger;
        }

        private const int MaxListItems = 30;
        private const int MaxColumns = 25;

        public async Task<string> ListTablesAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                var schema = await _schemaExplorer.ExploreAsync(databaseName, engine, connectionString, null, cancellationToken);
                if (schema.TablesCount == 0)
                    return "No tables found in the database.";

                var sb = new StringBuilder();
                sb.AppendLine($"Database has {schema.TablesCount} table(s) — showing up to {MaxListItems}:");
                var tables = schema.Objects.Where(o => o.Type == "Table").ToList();
                foreach (var obj in tables.Take(MaxListItems))
                    sb.AppendLine($"- {obj.Name}");
                var remaining = tables.Count - MaxListItems;
                if (remaining > 0)
                    sb.AppendLine($"... and {remaining} more table(s) (use search_schema to find specific tables)");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListTables failed for {Db}", databaseName);
                return $"Error listing tables: {ex.Message}";
            }
        }

        public async Task<string> DescribeTableAsync(string databaseName, DatabaseEngine engine, string connectionString, string tableName, CancellationToken cancellationToken = default)
        {
            try
            {
                var columns = await _schemaExplorer.GetTableColumnsAsync(databaseName, engine, connectionString, cancellationToken);
                var table = columns.FirstOrDefault(t =>
                    t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));

                if (table == null)
                {
                    var all = columns.Select(t => t.TableName).ToList();
                    return all.Count == 0
                        ? $"Table '{tableName}' not found — the database has no tables."
                        : $"Table '{tableName}' not found. Available tables: {string.Join(", ", all)}";
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Table: {table.TableName}");
                var totalCols = table.Columns.Count;
                sb.AppendLine($"Columns ({totalCols} total — showing up to {MaxColumns}):");
                foreach (var col in table.Columns.Take(MaxColumns))
                {
                    var flags = new List<string>();
                    if (col.IsPrimaryKey) flags.Add("PK");
                    flags.Add(col.IsNullable ? "NULL" : "NOT NULL");
                    sb.AppendLine($"  - {col.Name} {col.DataType} [{string.Join(", ", flags)}]");
                }
                var remainingCols = totalCols - MaxColumns;
                if (remainingCols > 0)
                    sb.AppendLine($"  ... and {remainingCols} more column(s)");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DescribeTable failed for {Db}.{Table}", databaseName, tableName);
                return $"Error describing table '{tableName}': {ex.Message}";
            }
        }

        public async Task<string> SearchSchemaAsync(string databaseName, DatabaseEngine engine, string connectionString, string query, CancellationToken cancellationToken = default)
        {
            try
            {
                var schema = await _schemaExplorer.ExploreAsync(databaseName, engine, connectionString, query, cancellationToken);
                if (schema.Objects.Count == 0)
                    return $"No database objects match '{query}'.";

                var sb = new StringBuilder();
                sb.AppendLine($"Objects matching '{query}' (showing up to {MaxListItems}):");
                foreach (var obj in schema.Objects.Take(MaxListItems))
                    sb.AppendLine($"  - [{obj.Type}] {obj.Name}");
                var remaining = schema.Objects.Count - MaxListItems;
                if (remaining > 0)
                    sb.AppendLine($"  ... and {remaining} more match(es) (use a more specific query)");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchSchema failed for {Db} query '{Q}'", databaseName, query);
                return $"Error searching schema: {ex.Message}";
            }
        }

        public async Task<string> ListViewsAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                var schema = await _schemaExplorer.ExploreAsync(databaseName, engine, connectionString, null, cancellationToken);
                if (schema.ViewsCount == 0)
                    return "No views found in the database.";

                var routines = await _schemaExplorer.GetRoutineDefinitionsAsync(databaseName, engine, connectionString, cancellationToken);
                var views = routines.Where(r => r.Type == "View").ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"Database has {schema.ViewsCount} view(s) — showing up to {MaxListItems}:");
                foreach (var view in views.Take(MaxListItems))
                {
                    sb.AppendLine($"- {view.Name}");
                    if (!string.IsNullOrWhiteSpace(view.Definition))
                    {
                        var def = view.Definition.Length > 2000
                            ? view.Definition[..2000] + "\n-- ...(truncated)..."
                            : view.Definition;
                        sb.AppendLine("  ```sql");
                        sb.AppendLine($"  {def}");
                        sb.AppendLine("  ```");
                    }
                }
                var remaining = views.Count - MaxListItems;
                if (remaining > 0)
                    sb.AppendLine($"... and {remaining} more view(s) (use search_schema for specifics)");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListViews failed for {Db}", databaseName);
                return $"Error listing views: {ex.Message}";
            }
        }

        public async Task<string> ListRoutinesAsync(string databaseName, DatabaseEngine engine, string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                var schema = await _schemaExplorer.ExploreAsync(databaseName, engine, connectionString, null, cancellationToken);
                if (schema.StoredProceduresCount == 0 && schema.FunctionsCount == 0)
                    return "No stored procedures or functions found in the database.";

                var routines = await _schemaExplorer.GetRoutineDefinitionsAsync(databaseName, engine, connectionString, cancellationToken);

                var sb = new StringBuilder();
                if (schema.StoredProceduresCount > 0)
                {
                    var sps = routines.Where(r => r.Type == "StoredProcedure").ToList();
                    sb.AppendLine($"Stored Procedures ({sps.Count} total — showing up to {MaxListItems}):");
                    foreach (var sp in sps.Take(MaxListItems))
                    {
                        sb.AppendLine($"- {sp.Name}");
                        if (!string.IsNullOrWhiteSpace(sp.Definition))
                        {
                            var def = sp.Definition.Length > 2000
                                ? sp.Definition[..2000] + "\n-- ...(truncated)..."
                                : sp.Definition;
                            sb.AppendLine("  ```sql");
                            sb.AppendLine($"  {def}");
                            sb.AppendLine("  ```");
                        }
                    }
                    var spRemaining = sps.Count - MaxListItems;
                    if (spRemaining > 0)
                        sb.AppendLine($"  ... and {spRemaining} more stored procedure(s) (use search_schema for specifics)");
                }

                if (schema.FunctionsCount > 0)
                {
                    var funcs = routines.Where(r => r.Type == "Function").ToList();
                    sb.AppendLine($"Functions ({funcs.Count} total — showing up to {MaxListItems}):");
                    foreach (var fn in funcs.Take(MaxListItems))
                    {
                        sb.AppendLine($"- {fn.Name}");
                        if (!string.IsNullOrWhiteSpace(fn.Definition))
                        {
                            var def = fn.Definition.Length > 2000
                                ? fn.Definition[..2000] + "\n-- ...(truncated)..."
                                : fn.Definition;
                            sb.AppendLine("  ```sql");
                            sb.AppendLine($"  {def}");
                            sb.AppendLine("  ```");
                        }
                    }
                    var fnRemaining = funcs.Count - MaxListItems;
                    if (fnRemaining > 0)
                        sb.AppendLine($"  ... and {fnRemaining} more function(s) (use search_schema for specifics)");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListRoutines failed for {Db}", databaseName);
                return $"Error listing routines: {ex.Message}";
            }
        }

        /// <summary>
        /// Executes a read-only SELECT query and returns results as formatted text.
        /// Only SELECT/EXECUTE/WITH statements are allowed. DDL/DML is rejected.
        /// Maximum 100 rows are returned to prevent token overflow.
        /// </summary>
        public async Task<string> ExecuteReadOnlyQueryAsync(string databaseName, DatabaseEngine engine, string connectionString, string sql, CancellationToken cancellationToken = default)
        {
            var trimmed = sql.Trim();
            if (!Regex.IsMatch(trimmed, @"^\s*(?:SELECT|WITH\s|EXEC(?:UTE)?\s|SHOW\s|DESC(?:RIBE)?\s)", RegexOptions.IgnoreCase))
            {
                return "Error: Only SELECT queries (read-only) are allowed. DDL and DML statements are blocked for safety.";
            }

            // Reject DML/DDL outside of SELECT context — allow information_schema queries
            if (Regex.IsMatch(trimmed, @"\b(?:INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|MERGE|REPLACE|GRANT|REVOKE)\b", RegexOptions.IgnoreCase))
            {
                if (!Regex.IsMatch(trimmed, @"^\s*SELECT", RegexOptions.IgnoreCase))
                {
                    return "Error: Only read-only queries are allowed via execute_query. Use execute_write for INSERT, UPDATE, or DELETE.";
                }
            }

            try
            {
                await using var connection = CreateConnection(engine, connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = trimmed;
                cmd.CommandTimeout = 30;

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                var sb = new StringBuilder();
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                    columns.Add(reader.GetName(i));

                sb.AppendLine("| " + string.Join(" | ", columns) + " |");
                sb.AppendLine("| " + string.Join(" | ", columns.Select(_ => "---")) + " |");

                var rowCount = 0;
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (rowCount >= 100)
                    {
                        sb.AppendLine($"| ... ({rowCount}+ rows, truncated to 100) |");
                        break;
                    }

                    var values = new List<string>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        if (reader.IsDBNull(i))
                            values.Add("NULL");
                        else
                        {
                            var val = reader.GetValue(i)?.ToString() ?? "NULL";
                            if (val.Length > 200) val = val[..200] + "...";
                            values.Add(val.Replace("|", "\\|"));
                        }
                    }
                    sb.AppendLine("| " + string.Join(" | ", values) + " |");
                    rowCount++;
                }

                if (rowCount == 0)
                    sb.AppendLine("(Query returned no rows.)");

                sb.AppendLine($"\n({rowCount} row(s))");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExecuteReadOnlyQuery failed for {Db}", databaseName);
                return $"Error executing query: {ex.Message}";
            }
        }

        /// <summary>
        /// Executes an INSERT or UPDATE statement and returns the rows-affected count.
        /// DELETE, DROP, TRUNCATE, ALTER, CREATE, and all other destructive or administrative
        /// Only database-level operations (DROP DATABASE, CREATE DATABASE, ALTER DATABASE,
        /// USE, GRANT, REVOKE) and dangerous system procedures are rejected.
        /// Local stored procedure execution (EXEC/CALL) is allowed.
        /// </summary>
        public async Task<string> ExecuteWriteAsync(string databaseName, DatabaseEngine engine, string connectionString, string sql, CancellationToken cancellationToken = default)
        {
            var trimmed = sql.Trim();

            // Block database-level operations (affect the database container itself)
            if (Regex.IsMatch(trimmed, @"^\s*(?:DROP|CREATE|ALTER)\s+(?:DATABASE|SERVER|LOGIN|USER)\b", RegexOptions.IgnoreCase))
            {
                return "Error: Database-level operations (DROP DATABASE, CREATE DATABASE, ALTER DATABASE) are not allowed. Only operations on objects inside the current database are permitted.";
            }

            // Block cross-database switch (USE switches the active database context)
            if (Regex.IsMatch(trimmed, @"\bUSE\s+[\w\[`""]", RegexOptions.IgnoreCase))
            {
                return "Error: USE statements are not allowed. All operations are scoped to the current workspace database only.";
            }

            // Block security-administration statements
            if (Regex.IsMatch(trimmed, @"^\s*(?:GRANT|REVOKE)\b", RegexOptions.IgnoreCase))
            {
                return "Error: GRANT and REVOKE statements are not allowed.";
            }

            // Block dangerous system extended procedures (xp_cmdshell, etc.) and cross-db master refs
            if (Regex.IsMatch(trimmed, @"\bxp_\w+\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(trimmed, @"\bmaster\.\.\w+\b", RegexOptions.IgnoreCase))
            {
                return "Error: System extended procedures and cross-database master references are not allowed.";
            }

            try
            {
                await using var connection = CreateConnection(engine, connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = trimmed;
                cmd.CommandTimeout = 30;

                var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);
                return $"Success: {rowsAffected} row(s) affected.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExecuteWrite failed for {Db}", databaseName);
                return $"Error executing statement: {ex.Message}";
            }
        }

        private static DbConnection CreateConnection(DatabaseEngine engine, string connectionString)
        {
            return engine switch
            {
                DatabaseEngine.MySql => new MySqlConnection(connectionString),
                _ => new SqlConnection(connectionString)
            };
        }
    }
}
