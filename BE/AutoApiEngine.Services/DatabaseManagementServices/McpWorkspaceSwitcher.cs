using AutoApiEngine.ServiceAbstraction;
using Microsoft.Extensions.Logging;

namespace AutoApiEngine.Services.DatabaseManagementServices;

/// <summary>
/// Implements IMcpWorkspaceSwitcher by using McpConfigBuilder to build the config
/// and accepting a restart callback to restart the OpenCode server.
/// </summary>
public class McpWorkspaceSwitcher : IMcpWorkspaceSwitcher
{
    private readonly McpConfigBuilder _configBuilder;
    private readonly Func<CancellationToken, Task> _restartCallback;
    private readonly ILogger<McpWorkspaceSwitcher> _logger;

    public McpWorkspaceSwitcher(
        McpConfigBuilder configBuilder,
        Func<CancellationToken, Task> restartCallback,
        ILogger<McpWorkspaceSwitcher> logger)
    {
        _configBuilder = configBuilder;
        _restartCallback = restartCallback;
        _logger = logger;
    }

    public async Task<(string configPath, string packageName)?> SwitchWorkspaceAsync(string workspaceId, CancellationToken ct = default)
    {
        _logger.LogInformation("Switching MCP to workspace {WorkspaceId}...", workspaceId);

        var result = await _configBuilder.BuildConfigAsync(workspaceId, ct);
        if (result == null)
        {
            _logger.LogWarning("Could not build MCP config for workspace {WorkspaceId}", workspaceId);
            return null;
        }

        // Restart OpenCode server to pick up new MCP config
        try
        {
            using var restartCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            restartCts.CancelAfter(TimeSpan.FromSeconds(30));
            await _restartCallback(restartCts.Token);
            _logger.LogInformation("OpenCode server restarted for workspace {WorkspaceId}", workspaceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart OpenCode server after MCP config change for workspace {WorkspaceId}", workspaceId);
            // Config was written, so it will take effect on next manual restart
        }

        return (result.Value.configPath, result.Value.mcpPackage);
    }
}
