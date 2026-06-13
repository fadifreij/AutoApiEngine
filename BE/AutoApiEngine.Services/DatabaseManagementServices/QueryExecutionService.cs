using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Executes arbitrary SQL queries against a workspace's database.
    /// Handles both result-set queries (SELECT/EXEC/WITH) and non-result-set queries
    /// (INSERT/UPDATE/DELETE) intelligently.
    /// </summary>
    public partial class QueryExecutionService : IQueryExecutionService
    {
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<QueryExecutionService> _logger;

        // Regex to detect if a SQL statement produces a result set
        // Matches: SELECT, WITH (CTE), EXEC, EXECUTE, or SET (FMTONLY etc.)
        [GeneratedRegex(@"^\s*(?:SELECT|WITH\s|EXEC(?:UTE)?\s|SET\s)", RegexOptions.IgnoreCase | RegexOptions.Compiled, 1000)]
        private static partial Regex ResultSetQueryPattern();

        public QueryExecutionService(
            IWorkspaceRepository workspaceRepository,
            IConfiguration configuration,
            ILogger<QueryExecutionService> logger)
        {
            _workspaceRepository = workspaceRepository;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<QueryExecutionResponse> ExecuteAsync(
            QueryExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Sql))
                throw new ArgumentException("SQL query cannot be empty.");

            var workspace = await _workspaceRepository.GetByIdAsync(request.WorkspaceId, cancellationToken)
                ?? throw new KeyNotFoundException($"Workspace '{request.WorkspaceId}' not found.");

            var engine = workspace.DatabaseEngine;
            var connectionString = BuildConnectionString(engine, workspace);

            var sw = Stopwatch.StartNew();

            try
            {
                // Preprocess MySQL DELIMITER directives for stored procedures, functions, etc.
                var processedSql = engine == DatabaseEngine.MySql
                    ? PreprocessDelimiter(request.Sql)
                    : request.Sql;

                // Detect if this is a result-set query
                var firstStatement = GetFirstStatement(processedSql);
                var isSelect = firstStatement != null && ResultSetQueryPattern().IsMatch(firstStatement);

                await using var connection = CreateConnection(engine, connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = processedSql;
                cmd.CommandTimeout = 120; // 2 minutes max

                if (isSelect)
                {
                    return await ExecuteReaderAsync(cmd, sw, cancellationToken);
                }
                else
                {
                    return await ExecuteNonQueryAsync(cmd, sw, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                _logger.LogInformation("Query execution was cancelled after {DurationMs}ms", sw.ElapsedMilliseconds);
                return new QueryExecutionResponse
                {
                    Success = false,
                    Error = "Query execution was cancelled.",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Query execution failed after {DurationMs}ms", sw.ElapsedMilliseconds);
                return new QueryExecutionResponse
                {
                    Success = false,
                    Error = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }

        private async Task<QueryExecutionResponse> ExecuteReaderAsync(
            DbCommand cmd,
            Stopwatch sw,
            CancellationToken cancellationToken)
        {
            var response = new QueryExecutionResponse
            {
                Success = true,
                IsSelectQuery = true
            };

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            // Read column metadata
            var columnSchema = await reader.GetColumnSchemaAsync(cancellationToken);
            foreach (var col in columnSchema)
            {
                response.Columns.Add(new QueryResultColumnDto
                {
                    Name = col.ColumnName ?? col.BaseColumnName ?? $"Column{response.Columns.Count}",
                    DataType = col.DataTypeName ?? col.DataType?.Name ?? "unknown"
                });
            }

            // Read data rows
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new QueryResultRowDto();
                for (int i = 0; i < response.Columns.Count; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        row.Values.Add(null);
                    }
                    else
                    {
                        var value = reader.GetValue(i);
                        row.Values.Add(value?.ToString());
                    }
                }
                response.Rows.Add(row);
            }

            sw.Stop();
            response.DurationMs = sw.ElapsedMilliseconds;
            return response;
        }

        private async Task<QueryExecutionResponse> ExecuteNonQueryAsync(
            DbCommand cmd,
            Stopwatch sw,
            CancellationToken cancellationToken)
        {
            var response = new QueryExecutionResponse
            {
                Success = true,
                IsSelectQuery = false
            };

            response.RowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);

            sw.Stop();
            response.DurationMs = sw.ElapsedMilliseconds;
            return response;
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
                    var csb = new SqlConnectionStringBuilder(baseConnStr)
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

            var sqlCsb = new SqlConnectionStringBuilder(baseConnStr)
            {
                InitialCatalog = databaseName
            };
            return sqlCsb.ConnectionString;
        }

        private static DbConnection CreateConnection(DatabaseEngine engine, string connectionString)
        {
            return engine switch
            {
                DatabaseEngine.MySql => new MySqlConnection(connectionString),
                _ => new SqlConnection(connectionString)
            };
        }

        /// <summary>
        /// Extracts the first non-comment, non-empty SQL statement from the query text.
        /// Used to determine whether the query produces a result set.
        /// </summary>
        private static string? GetFirstStatement(string sql)
        {
            var cleaned = RemoveComments(sql);
            var statements = cleaned.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var stmt in statements)
            {
                var trimmed = stmt.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    return trimmed;
            }
            return null;
        }

        private static string RemoveComments(string sql)
        {
            // Remove single-line comments (-- ...)
            sql = Regex.Replace(sql, @"--[^\n]*", "");
            // Remove block comments (/* ... */)
            sql = Regex.Replace(sql, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return sql;
        }

        /// <summary>
        /// Strips MySQL DELIMITER directives and replaces custom delimiters with standard semicolons.
        /// DELIMITER is a mysql CLI command that the MySQL server does not understand.
        /// Without this preprocessing, CREATE PROCEDURE/FUNCTION/TRIGGER scripts fail when sent
        /// through the API (Query Studio) because the ADO.NET driver sends SQL directly to the server.
        /// </summary>
        private static string PreprocessDelimiter(string sql)
        {
            if (!sql.Contains("DELIMITER", StringComparison.OrdinalIgnoreCase))
                return sql;

            var lines = sql.Split('\n');
            var currentDelimiter = ";";
            var result = new System.Text.StringBuilder();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Check for DELIMITER directive: DELIMITER $$ or DELIMITER //
                var delimiterIndex = trimmed.IndexOf("DELIMITER ", StringComparison.OrdinalIgnoreCase);
                if (delimiterIndex >= 0 && (delimiterIndex == 0 || char.IsWhiteSpace(trimmed[delimiterIndex - 1])))
                {
                    currentDelimiter = trimmed[(delimiterIndex + "DELIMITER ".Length)..].Trim();
                    // If resetting to default semicolon, just skip the line
                    if (currentDelimiter == ";")
                        continue;
                    // If this is a standalone DELIMITER line, skip it
                    if (delimiterIndex == 0 || trimmed[..delimiterIndex].TrimEnd() == "")
                        continue;
                    // Otherwise, the DELIMITER is part of a comment or string — let it through
                }

                // If a custom delimiter is active, check if this line ends with it
                if (currentDelimiter != ";")
                {
                    var lineTrimmedEnd = trimmed.TrimEnd();
                    if (lineTrimmedEnd.EndsWith(currentDelimiter))
                    {
                        // Replace the custom delimiter with semicolon
                        result.AppendLine(lineTrimmedEnd[..^currentDelimiter.Length] + ";");
                        continue;
                    }
                }

                result.AppendLine(line);
            }

            return result.ToString().Trim();
        }
    }
}
