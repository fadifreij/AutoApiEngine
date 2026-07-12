using Microsoft.Extensions.Logging;

namespace AutoApiEngine.Services.DatabaseManagementServices;

/// <summary>
/// Service that manages MCP configuration per workspace.
/// Reads workspace record from MySQL system database and builds the appropriate MCP config.
/// </summary>
public interface IMcpWorkspaceService
{
    /// <summary>
    /// Sets the active workspace for MCP operations.
    /// Reads workspace from MySQL, builds MCP config, returns the config path and package name.
    /// </summary>
    Task<(string configPath, string mcpPackage)?> SetActiveWorkspaceAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current MCP config info for the active workspace.
    /// </summary>
    (string configPath, string mcpPackage)? GetCurrentMcpConfig();

    /// <summary>
    /// Clears the active workspace.
    /// </summary>
    void ClearActiveWorkspace();
}

public class McpWorkspaceService : IMcpWorkspaceService
{
    private readonly McpConfigBuilder _configBuilder;
    private readonly ILogger<McpWorkspaceService> _logger;

    private string? _currentConfigPath;
    private string? _currentMcpPackage;
    private string? _currentWorkspaceId;

    public McpWorkspaceService(McpConfigBuilder configBuilder, ILogger<McpWorkspaceService> logger)
    {
        _configBuilder = configBuilder;
        _logger = logger;
    }

    public async Task<(string configPath, string mcpPackage)?> SetActiveWorkspaceAsync(
        string workspaceId, CancellationToken ct = default)
    {
        var result = await _configBuilder.BuildConfigAsync(workspaceId, ct);
        if (result == null)
        {
            _logger.LogWarning("Could not build MCP config for workspace {WorkspaceId}", workspaceId);
            ClearActiveWorkspace();
            return null;
        }

        // Clean up previous YAML config (SQL Server only)
        if (!string.IsNullOrEmpty(_currentWorkspaceId) && _currentWorkspaceId != workspaceId)
        {
            _configBuilder.CleanupConfig(_currentWorkspaceId);
        }

        _currentConfigPath = result.Value.configPath;
        _currentMcpPackage = result.Value.mcpPackage;
        _currentWorkspaceId = workspaceId;

        _logger.LogInformation(
            "MCP workspace set to {WorkspaceId} (package: {Package}, config: {Config})",
            workspaceId, _currentMcpPackage, _currentConfigPath);

        return (_currentConfigPath, _currentMcpPackage);
    }

    public (string configPath, string mcpPackage)? GetCurrentMcpConfig()
    {
        if (_currentConfigPath == null || _currentMcpPackage == null) return null;
        return (_currentConfigPath, _currentMcpPackage);
    }

    public void ClearActiveWorkspace()
    {
        if (!string.IsNullOrEmpty(_currentWorkspaceId))
        {
            _configBuilder.CleanupConfig(_currentWorkspaceId);
        }

        _currentConfigPath = null;
        _currentMcpPackage = null;
        _currentWorkspaceId = null;

        _logger.LogInformation("MCP workspace cleared");
    }
}
