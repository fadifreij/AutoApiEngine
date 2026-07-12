namespace AutoApiEngine.ServiceAbstraction;

/// <summary>
/// Handles switching the MCP (Model Context Protocol) configuration to a different workspace.
/// Builds the per-workspace MCP config and triggers an OpenCode server restart.
/// </summary>
public interface IMcpWorkspaceSwitcher
{
    /// <summary>
    /// Switches MCP to the specified workspace.
    /// Builds config, updates opencode.json, restarts the OpenCode server.
    /// Returns (configPath, packageName) on success.
    /// </summary>
    Task<(string configPath, string packageName)?> SwitchWorkspaceAsync(string workspaceId, CancellationToken ct = default);
}
