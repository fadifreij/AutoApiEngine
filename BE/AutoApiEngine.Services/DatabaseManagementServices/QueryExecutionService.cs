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
                // Detect if this is a result-set query
                var firstStatement = GetFirstStatement(request.Sql);
                var isSelect = firstStatement != null && ResultSetQueryPattern().IsMatch(firstStatement);

                await using var connection = CreateConnection(engine, connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = request.Sql;
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
    }
}
