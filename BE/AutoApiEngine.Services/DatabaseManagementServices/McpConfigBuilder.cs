using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoApiEngine.Services.DatabaseManagementServices;

/// <summary>
/// Represents a workspace record from the MySQL system database.
/// </summary>
public class WorkspaceRecord
{
    public string Name { get; set; } = "";
    public string? ServerHost { get; set; }
    public string? DatabaseName { get; set; }
    public string? DbUserName { get; set; }
    public string? DbPassword { get; set; }
    public int DatabaseEngine { get; set; }
}

/// <summary>
/// MCP engine configuration mapping.
/// </summary>
public class McpEngineInfo
{
    public string PackageName { get; set; } = "";
    public string EntryScript { get; set; } = "dist/index.js";
    public int DefaultPort { get; set; } = 1433;
    public string[] SystemDatabases { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Builds dynamic MCP configuration per workspace.
/// Reads workspace record from MySQL system database (DbPortal).
/// Each user/workflow gets its own MCP config connecting to their database only.
///
/// Strategy per engine:
///   MySQL/PostgreSql → environment vars in opencode.json ("environment" field)
///   SQL Server       → YAML config file ("connection:" key) + --config arg
///   Sqlite           → environment vars in opencode.json
/// </summary>
public class McpConfigBuilder
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<McpConfigBuilder> _logger;
    private readonly string _opencodeDir;

    // DatabaseEngine enum values from the domain model
    // 0 = SqlServer, 1 = PostgreSql, 2 = MySql, 3 = Sqlite
    private static readonly Dictionary<int, McpEngineInfo> Engines = new()
    {
        [0] = new McpEngineInfo
        {
            PackageName = "@tugberkgunver/mcp-sqlserver",
            DefaultPort = 1433,
            SystemDatabases = new[] { "master", "msdb", "tempdb", "model" }
        },
        [1] = new McpEngineInfo
        {
            PackageName = "@benborla29/mcp-server-mysql",
            DefaultPort = 5432,
            SystemDatabases = new[] { "postgres", "template0", "template1" }
        },
        [2] = new McpEngineInfo
        {
            PackageName = "@benborla29/mcp-server-mysql",
            DefaultPort = 3306,
            SystemDatabases = new[] { "information_schema", "performance_schema", "mysql", "sys" }
        },
        [3] = new McpEngineInfo
        {
            PackageName = "@tugberkgunver/mcp-sqlite",
            DefaultPort = 0,
            SystemDatabases = Array.Empty<string>()
        }
    };

    public McpConfigBuilder(IConfiguration configuration, ILogger<McpConfigBuilder> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var workspaceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        _opencodeDir = Path.Combine(workspaceRoot, "opencode");
    }

    /// <summary>
    /// Fetches workspace record from MySQL system database.
    /// </summary>
    public async Task<WorkspaceRecord?> GetWorkspaceAsync(string workspaceId)
    {
        var connStr = _configuration.GetConnectionString("MySqlConnection");
        if (string.IsNullOrEmpty(connStr))
        {
            _logger.LogError("MySqlConnection string not configured");
            return null;
        }

        _logger.LogDebug("Fetching workspace {WorkspaceId} from MySQL for MCP config", workspaceId);

        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT Name, ServerHost, DatabaseName, DbUserName, DbPassword, DatabaseEngine " +
            "FROM Workspaces WHERE Id = @id", conn);
        cmd.Parameters.AddWithValue("@id", workspaceId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var record = new WorkspaceRecord
            {
                Name = reader.GetString(0),                                           // Name
                ServerHost = reader.IsDBNull(1) ? null : reader.GetString(1),         // ServerHost
                DatabaseName = reader.IsDBNull(2) ? null : reader.GetString(2),       // DatabaseName
                DbUserName = reader.IsDBNull(3) ? null : reader.GetString(3),         // DbUserName
                DbPassword = reader.IsDBNull(4) ? null : reader.GetString(4),         // DbPassword
                DatabaseEngine = reader.GetInt32(5)                                    // DatabaseEngine
            };
            _logger.LogDebug("Found workspace {Name} (Engine: {Engine}, DB: {Database})",
                record.Name, record.DatabaseEngine, record.DatabaseName);
            return record;
        }

        _logger.LogWarning("Workspace {WorkspaceId} not found in MySQL database", workspaceId);
        return null;
    }

    /// <summary>
    /// Builds MCP config for a workspace.
    /// For MySQL/PostgreSql/Sqlite: writes env vars directly into opencode.json "environment" field.
    /// For SqlServer: generates a YAML config file AND writes the opencode.json entry with --config arg.
    /// Returns (configPath, mcpPackage).
    /// </summary>
    public async Task<(string configPath, string mcpPackage)?> BuildConfigAsync(
        string workspaceId,
        CancellationToken ct = default)
    {
        var workspace = await GetWorkspaceAsync(workspaceId);
        if (workspace == null)
        {
            _logger.LogWarning("Workspace {WorkspaceId} not found in MySQL", workspaceId);
            return null;
        }

        if (!Engines.TryGetValue(workspace.DatabaseEngine, out var engineInfo))
        {
            _logger.LogWarning("Unknown engine {Engine} for workspace {WorkspaceId}", workspace.DatabaseEngine, workspaceId);
            return null;
        }

        // Resolve connection details
        var (host, port, db) = ResolveConnection(workspace, engineInfo);
        var (user, password) = ResolveCredentials(workspace);

        // For SQL Server: generate YAML config file (for trustServerCertificate etc.)
        string? yamlConfigPath = null;
        if (workspace.DatabaseEngine == 0)
        {
            yamlConfigPath = BuildSqlServerYamlConfig(workspaceId, host, port, db, user, password, engineInfo);
        }

        // Update opencode.json MCP entry
        var mcpName = GetMcpName(workspace.DatabaseEngine);
        await UpdateOpencodeJsonAsync(workspace.DatabaseEngine, yamlConfigPath, host, port, db, user, password, ct);

        _logger.LogInformation(
            "MCP config built for workspace {Name} (Engine: {Engine}, Server: {Host}:{Port}, DB: {Db})",
            workspace.Name, workspace.DatabaseEngine, host, port, db);

        return (yamlConfigPath ?? "opencode.json", engineInfo.PackageName);
    }

    // ── opencode.json Update ──

    /// <summary>
    /// Updates opencode.json MCP section with the correct command, environment, and (if SQL Server) config arg.
    /// </summary>
    private async Task UpdateOpencodeJsonAsync(
        int databaseEngine, string? yamlConfigPath,
        string host, int port, string db, string? user, string? password,
        CancellationToken ct)
    {
        var opencodeJsonPath = Path.Combine(_opencodeDir, "opencode.json");
        if (!File.Exists(opencodeJsonPath))
        {
            _logger.LogWarning("opencode.json not found at {Path}, skipping MCP update", opencodeJsonPath);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(opencodeJsonPath, ct);
            var doc = JsonNode.Parse(json) as JsonObject;
            if (doc == null) return;

            // Ensure "mcp" section exists
            if (doc["mcp"] is not JsonObject mcpSection)
            {
                mcpSection = new JsonObject();
                doc["mcp"] = mcpSection;
            }

            // Remove all existing MCP entries
            var keysToRemove = mcpSection.Select(k => k.Key).ToList();
            foreach (var key in keysToRemove)
                mcpSection.Remove(key);

            var engineInfo = Engines[databaseEngine];
            var mcpName = GetMcpName(databaseEngine);

            // Build the entry based on engine type
            JsonObject entry;
            if (databaseEngine == 0)
            {
                // SQL Server: use YAML config file with --config arg
                entry = BuildSqlServerOpencodeEntry(engineInfo, yamlConfigPath!);
            }
            else if (databaseEngine == 1 || databaseEngine == 2)
            {
                // MySQL / PostgreSql: use environment vars (no YAML file)
                entry = BuildMySqlOpencodeEntry(engineInfo, host, port, db, user ?? "root", password ?? "");
            }
            else if (databaseEngine == 3)
            {
                // Sqlite: use environment vars
                entry = BuildSqliteOpencodeEntry(engineInfo, db);
            }
            else
            {
                entry = BuildMySqlOpencodeEntry(engineInfo, host, port, db, user ?? "root", password ?? "");
            }

            mcpSection[mcpName] = entry;

            // Write back
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = doc.ToJsonString(options);
            await File.WriteAllTextAsync(opencodeJsonPath, updatedJson, ct);

            _logger.LogInformation("Updated opencode.json MCP entry '{Name}' for engine {Engine}",
                mcpName, databaseEngine);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update opencode.json MCP config");
        }
    }

    // ── OpenCode Entry Builders ──

    /// <summary>
    /// Builds opencode.json MCP entry for MySQL using "environment" field.
    /// </summary>
    private static JsonObject BuildMySqlOpencodeEntry(
        McpEngineInfo engine, string host, int port, string db, string user, string password)
    {
        var cwd = Path.GetFullPath(Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
            "opencode"));

        return new JsonObject
        {
            ["type"] = "local",
            ["command"] = new JsonArray { "node", $"node_modules/{engine.PackageName}/{engine.EntryScript}" },
            ["environment"] = new JsonObject
            {
                ["MYSQL_HOST"] = host,
                ["MYSQL_PORT"] = port.ToString(),
                ["MYSQL_USER"] = user,
                ["MYSQL_PASS"] = password,
                ["MYSQL_DB"] = db,
                ["ALLOW_INSERT_OPERATION"] = "true",
                ["ALLOW_UPDATE_OPERATION"] = "true",
                ["ALLOW_DELETE_OPERATION"] = "true"
            },
            ["cwd"] = cwd,
            ["enabled"] = true
        };
    }

    /// <summary>
    /// Builds opencode.json MCP entry for SQL Server using YAML config file.
    /// </summary>
    private static JsonObject BuildSqlServerOpencodeEntry(McpEngineInfo engine, string configPath)
    {
        var cwd = Path.GetFullPath(Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
            "opencode"));

        return new JsonObject
        {
            ["type"] = "local",
            ["command"] = new JsonArray { "node", $"node_modules/{engine.PackageName}/{engine.EntryScript}" },
            ["args"] = new JsonArray { "--config", configPath },
            ["cwd"] = cwd,
            ["enabled"] = true
        };
    }

    /// <summary>
    /// Builds opencode.json MCP entry for Sqlite using "environment" field.
    /// </summary>
    private static JsonObject BuildSqliteOpencodeEntry(McpEngineInfo engine, string dbPath)
    {
        var cwd = Path.GetFullPath(Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
            "opencode"));

        return new JsonObject
        {
            ["type"] = "local",
            ["command"] = new JsonArray { "node", $"node_modules/{engine.PackageName}/{engine.EntryScript}" },
            ["environment"] = new JsonObject
            {
                ["SQLITE_DB_PATH"] = dbPath
            },
            ["cwd"] = cwd,
            ["enabled"] = true
        };
    }

    // ── Connection Resolution ──

    private (string host, int port, string database) ResolveConnection(WorkspaceRecord ws, McpEngineInfo engine)
    {
        // If ServerHost is empty → client database is on the SAME server as the system MySQL
        if (string.IsNullOrWhiteSpace(ws.ServerHost))
        {
            // System MySQL server: localhost:3307
            var sysHost = _configuration["MySqlServerName"] ?? "localhost";
            var sysPort = 3306;
            if (sysHost.Contains(':'))
            {
                var parts = sysHost.Split(':');
                sysHost = parts[0];
                if (int.TryParse(parts[1], out var p)) sysPort = p;
            }
            return (sysHost, sysPort, ws.DatabaseName ?? "");
        }

        // ServerHost may contain port: "server:1433"
        var hostParts = ws.ServerHost.Split(':');
        var host = hostParts[0];
        var port = hostParts.Length > 1 && int.TryParse(hostParts[1], out var portVal) ? portVal : engine.DefaultPort;
        return (host, port, ws.DatabaseName ?? "");
    }

    private (string? user, string? password) ResolveCredentials(WorkspaceRecord ws)
    {
        // If DbUserName is empty → use system defaults (root/root for MySQL, Windows for SQL Server)
        if (string.IsNullOrWhiteSpace(ws.DbUserName))
        {
            if (ws.DatabaseEngine == 2)
                return ("root", "root"); // MySQL system default
            return (null, null); // Windows auth for SQL Server
        }

        return (ws.DbUserName, ws.DbPassword);
    }

    // ── YAML Config Builder (SQL Server only) ──

    private string BuildSqlServerYamlConfig(
        string workspaceId, string host, int port, string database,
        string? user, string? password, McpEngineInfo engine)
    {
        var yaml = new StringBuilder();
        yaml.AppendLine($"# Auto-generated for workspace: {workspaceId}");
        yaml.AppendLine($"# Engine: SqlServer | Generated: {DateTime.UtcNow:O}");
        yaml.AppendLine();
        yaml.AppendLine("connection:");
        yaml.AppendLine($"  host: {host}");
        yaml.AppendLine($"  port: {port}");
        yaml.AppendLine($"  database: {database}");
        yaml.AppendLine("  authentication:");

        if (!string.IsNullOrWhiteSpace(user))
        {
            // SQL auth
            yaml.AppendLine("    type: sql");
            yaml.AppendLine($"    user: {EscapeYaml(user)}");
            yaml.AppendLine($"    password: {EscapeYaml(password ?? "")}");
        }
        else
        {
            // Windows auth
            yaml.AppendLine("    type: windows");
        }

        yaml.AppendLine("  trustServerCertificate: true");
        yaml.AppendLine("  encrypt: false");
        yaml.AppendLine("  connectionTimeout: 15000");
        yaml.AppendLine("  requestTimeout: 30000");
        yaml.AppendLine("  pool:");
        yaml.AppendLine("    min: 0");
        yaml.AppendLine("    max: 5");
        yaml.AppendLine("    idleTimeout: 30000");

        AppendSecurity(yaml, engine.SystemDatabases);

        var path = GetConfigPath(workspaceId);
        File.WriteAllText(path, yaml.ToString());
        return path;
    }

    private void AppendSecurity(StringBuilder yaml, string[] systemDatabases)
    {
        yaml.AppendLine();
        yaml.AppendLine("security:");
        yaml.AppendLine("  mode: readwrite");
        yaml.AppendLine("  maxRowCount: 1000");
        yaml.AppendLine("  queryTimeout: 30000");
        yaml.AppendLine("  allowDDL: false");
        yaml.AppendLine("  allowMutations: true");

        if (systemDatabases.Length > 0)
        {
            yaml.AppendLine("  blockedDatabases:");
            foreach (var db in systemDatabases)
                yaml.AppendLine($"    - {db}");
        }

        yaml.AppendLine("  blockedKeywords:");
        yaml.AppendLine("    - xp_cmdshell");
        yaml.AppendLine("    - SHUTDOWN");
        yaml.AppendLine("    - DROP DATABASE");
        yaml.AppendLine("    - DROP TABLE");
        yaml.AppendLine("    - TRUNCATE TABLE");
        yaml.AppendLine("    - RECONFIGURE");
        yaml.AppendLine("    - sp_configure");
        yaml.AppendLine("    - RESTORE");
        yaml.AppendLine("    - BACKUP");

        yaml.AppendLine();
        yaml.AppendLine("logging:");
        yaml.AppendLine("  level: info");
    }

    // ── Helpers ──

    private static string GetMcpName(int databaseEngine) => databaseEngine switch
    {
        0 => "sqlserver",
        1 or 2 => "mysql",
        3 => "sqlite",
        _ => "mysql"
    };

    private string GetConfigPath(string workspaceId)
    {
        return Path.Combine(_opencodeDir, $"mcp-config-{workspaceId}.yaml");
    }

    private static string EscapeYaml(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(':') || value.Contains('#') || value.Contains('"') || value.Contains('\''))
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return value;
    }

    public void CleanupConfig(string workspaceId)
    {
        var path = GetConfigPath(workspaceId);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Cleaned up MCP config for workspace {WorkspaceId}", workspaceId);
        }
    }
}
