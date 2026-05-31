using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Diagnostics;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    public class DdlExecutionService : IDdlExecutionService
    {
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IConfiguration _configuration;

        public DdlExecutionService(IWorkspaceRepository workspaceRepository, IConfiguration configuration)
        {
            _workspaceRepository = workspaceRepository;
            _configuration = configuration;
        }

        public async Task<DdlExecutionResponse> ExecuteAsync(DdlExecutionRequest request, CancellationToken cancellationToken = default)
        {
            var response = new DdlExecutionResponse();

            var workspace = await _workspaceRepository.GetByIdAsync(request.WorkspaceId, cancellationToken);
            if (workspace == null)
                throw new KeyNotFoundException($"Workspace '{request.WorkspaceId}' not found.");

            var engine = workspace.DatabaseEngine;
            var connectionString = BuildConnectionString(engine, workspace);

            var statements = SplitSqlStatements(request.Sql);

            foreach (var (sql, index) in statements.Select((s, i) => (s.Trim(), i)))
            {
                if (string.IsNullOrWhiteSpace(sql))
                    continue;

                var result = new DdlStatementResult
                {
                    Index = index,
                    Sql = sql
                };

                var sw = Stopwatch.StartNew();
                try
                {
                    await using var connection = CreateConnection(engine, connectionString);
                    await connection.OpenAsync(cancellationToken);

                    await using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.CommandTimeout = 120; // 2 minutes per statement

                    result.RowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Error = ex.Message;
                }

                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                response.Statements.Add(result);
            }

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
                // Use workspace-specific credentials
                if (engine == DatabaseEngine.MySql)
                {
                    // Always append; MySQL.Data uses the last Database= parameter
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

            // No dedicated credentials — use default config
            if (engine == DatabaseEngine.MySql)
            {
                // Always append; MySQL.Data uses the last Database= parameter
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

        private static List<string> SplitSqlStatements(string sql)
        {
            var statements = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inString = false;
            char stringChar = '\'';
            bool inLineComment = false;
            bool inBlockComment = false;

            for (int i = 0; i < sql.Length; i++)
            {
                var c = sql[i];

                if (inLineComment)
                {
                    if (c == '\n')
                        inLineComment = false;
                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
                    {
                        inBlockComment = false;
                        i++; // skip '/'
                    }
                    continue;
                }

                if (!inString && c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    inLineComment = true;
                    i++; // skip second '-'
                    continue;
                }

                if (!inString && c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    inBlockComment = true;
                    i++; // skip '*'
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    if (!inString)
                    {
                        inString = true;
                        stringChar = c;
                    }
                    else if (c == stringChar)
                    {
                        // Check for escaped quote ''
                        if (i + 1 < sql.Length && sql[i + 1] == c)
                        {
                            current.Append(c);
                            current.Append(c);
                            i++;
                            continue;
                        }
                        inString = false;
                    }
                }

                if (!inString && c == ';')
                {
                    var trimmed = current.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        statements.Add(trimmed);
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            // Last statement
            var last = current.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(last))
                statements.Add(last);

            return statements;
        }
    }
}