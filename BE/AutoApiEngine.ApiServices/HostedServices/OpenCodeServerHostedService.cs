using System.Diagnostics;
using Microsoft.Extensions.Options;
using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.ApiServices.HostedServices;

/// <summary>
/// Auto-starts the local OpenCode server as a child process when the backend starts,
/// and shuts it down when the backend stops. Eliminates the need to manually run
/// <c>opencode serve --port 3000</c>.
///
/// Also exposes a restart method so the server can be restarted when the MCP config
/// changes (e.g., when a user switches workspaces).
/// </summary>
public sealed class OpenCodeServerHostedService : IHostedService, IDisposable
{
    private readonly ILogger<OpenCodeServerHostedService> _logger;
    private readonly OpencodeAiSettings _settings;
    private Process? _serverProcess;
    private readonly string _opencodeExe;
    private CancellationTokenSource? _healthCts;
    private readonly object _lock = new();

    public OpenCodeServerHostedService(
        IOptions<OpencodeAiSettings> settings,
        ILogger<OpenCodeServerHostedService> logger)
    {
        _logger = logger;
        _settings = settings.Value;
        _opencodeExe = FindOpencodeExecutable();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_opencodeExe))
        {
            _logger.LogWarning(
                "OpenCode CLI not found. The backend AI service will fail to connect. " +
                "Install locally: cd opencode && npm install");
            return Task.CompletedTask;
        }

        StartServer();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopServerAsync();
    }

    /// <summary>
    /// Restarts the OpenCode server. Called when MCP config changes (workspace switch).
    /// Waits for the server to become healthy before returning.
    /// </summary>
    public async Task RestartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Restarting OpenCode server due to MCP config change...");

        await StopServerAsync();

        // Small delay to let port release
        await Task.Delay(500, ct);

        StartServer();

        // Wait for health
        var uri = new Uri(_settings.BaseUrl.TrimEnd('/'));
        await WaitForHealthAsync(uri, ct);

        _logger.LogInformation("OpenCode server restarted and healthy.");
    }

    /// <summary>
    /// Returns the path to the opencode/ directory (where opencode.json with MCP config lives).
    /// </summary>
    public string GetOpenCodeDirectory()
    {
        return FindOpenCodeDirectory();
    }

    // ── Private: Start / Stop ──

    private void StartServer()
    {
        if (string.IsNullOrEmpty(_opencodeExe))
        {
            _logger.LogWarning("OpenCode CLI not found. Cannot start server.");
            return;
        }

        var uri = new Uri(_settings.BaseUrl.TrimEnd('/'));
        var hostname = uri.Host;
        var port = uri.Port;

        _logger.LogInformation(
            "Starting local OpenCode server on {Scheme}://{Host}:{Port} (model: {Model})...",
            uri.Scheme, hostname, port, _settings.Model);

        try
        {
            var workDir = FindOpenCodeDirectory();
            _logger.LogInformation("OpenCode server WorkingDirectory: {Dir}", workDir);

            var psi = new ProcessStartInfo
            {
                FileName = _opencodeExe,
                Arguments = $"serve --port {port} --hostname {hostname} --pure",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workDir
            };

            // Set the password for the OpenCode server
            var password = !string.IsNullOrWhiteSpace(_settings.ApiKey)
                ? _settings.ApiKey
                : "local-dev-key";
            psi.EnvironmentVariables["OPENCODE_SERVER_PASSWORD"] = password;

            _serverProcess = new Process { StartInfo = psi };

            _serverProcess.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogDebug("OpenCode: {Msg}", e.Data);
            };
            _serverProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogWarning("OpenCode: {Msg}", e.Data);
            };

            if (!_serverProcess.Start())
            {
                _logger.LogError("Failed to start OpenCode server process.");
                _serverProcess = null;
                return;
            }

            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            _logger.LogInformation(
                "OpenCode server started (PID: {Pid}). Waiting for it to become healthy...",
                _serverProcess.Id);

            _healthCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            _ = WaitForHealthAsync(uri, _healthCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start OpenCode server process. " +
                "Install locally: cd opencode && npm install");
        }
    }

    private Task StopServerAsync()
    {
        _healthCts?.Cancel();

        // Kill tracked process
        if (_serverProcess is { HasExited: false })
        {
            _logger.LogInformation("Shutting down OpenCode server (PID: {Pid})...", _serverProcess.Id);
            try
            {
                _serverProcess.Kill(entireProcessTree: true);
                _serverProcess.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while stopping tracked OpenCode process.");
            }
        }

        _serverProcess?.Dispose();
        _serverProcess = null;

        // Also kill any orphaned opencode processes on our port
        KillOrphanedProcesses();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Kills any orphaned opencode.exe processes listening on the configured port.
    /// Handles cases where Visual Studio terminates the backend without cleanup.
    /// </summary>
    private void KillOrphanedProcesses()
    {
        var uri = new Uri(_settings.BaseUrl.TrimEnd('/'));
        var port = uri.Port;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            var netstat = Process.Start(psi);
            if (netstat == null) return;

            var output = netstat.StandardOutput.ReadToEnd();
            netstat.WaitForExit(3000);

            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains($":{port} ") || !line.Contains("LISTENING"))
                    continue;

                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;

                var pidStr = parts[^1];
                if (!int.TryParse(pidStr, out var pid)) continue;
                if (pid <= 4) continue; // skip system processes

                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (proc.ProcessName.Contains("opencode", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Killing orphaned OpenCode process PID={Pid} on port {Port}.", pid, port);
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Process already exited or access denied
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check for orphaned OpenCode processes.");
        }
    }

    public void Dispose()
    {
        _healthCts?.Cancel();
        _healthCts?.Dispose();
        KillOrphanedProcesses();
        _serverProcess?.Dispose();
    }

    // ── Health check ──

    private async Task WaitForHealthAsync(Uri uri, CancellationToken ct)
    {
        var healthUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}/global/health";
        var password = !string.IsNullOrWhiteSpace(_settings.ApiKey)
            ? _settings.ApiKey
            : "local-dev-key";

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        for (int attempt = 1; attempt <= 30; attempt++)
        {
            if (ct.IsCancellationRequested) return;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, healthUrl);
                var auth = Convert.ToBase64String(
                    System.Text.Encoding.ASCII.GetBytes($"opencode:{password}"));
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");

                using var response = await httpClient.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "OpenCode server is healthy on {Url} (attempt {Attempt}).",
                        healthUrl, attempt);
                    return;
                }
            }
            catch
            {
                // Not ready yet
            }

            _logger.LogDebug("Waiting for OpenCode server... attempt {Attempt}/30", attempt);
            await Task.Delay(1000, ct);
        }

        _logger.LogWarning(
            "OpenCode server did not become healthy after 30 attempts at {Url}. " +
            "The AI service may fail until it is available.", healthUrl);
    }

    // ── Helper: find opencode/ directory (where opencode.json with MCP config lives) ──

    private string FindOpenCodeDirectory()
    {
        // 1. Explicit config (appsettings.json or env var) — always wins
        if (!string.IsNullOrWhiteSpace(_settings.OpenCodeDir))
        {
            if (File.Exists(Path.Combine(_settings.OpenCodeDir, "opencode.json")))
                return _settings.OpenCodeDir;
        }

        // 2. Relative to executable — works in both dev and published
        var exeDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(exeDir, "opencode");
        if (File.Exists(Path.Combine(candidate, "opencode.json")))
            return candidate;

        // 3. Search up the tree (up to 6 levels) — catches dev layout
        var current = exeDir;
        for (int i = 0; i < 6; i++)
        {
            current = Path.GetDirectoryName(current);
            if (current == null) break;

            candidate = Path.Combine(current, "opencode");
            if (File.Exists(Path.Combine(candidate, "opencode.json")))
                return candidate;
        }

        // 4. Fallback — return expected path (will warn at startup)
        return Path.Combine(exeDir, "opencode");
    }

    // ── Helper: find opencode executable ──

    private static string FindOpencodeExecutable()
    {
        // 1. Local install in opencode/node_modules/opencode-ai/bin/ (dedicated instance — preferred)
        var opencodeDir = FindOpenCodeDirectoryStatic();
        var localExe = Path.Combine(opencodeDir, "node_modules", "opencode-ai", "bin", "opencode.exe");
        if (File.Exists(localExe))
            return Path.GetFullPath(localExe);

        // 2. Global npm install
        var globalExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "opencode-ai", "bin", "opencode.exe");
        if (File.Exists(globalExe))
            return Path.GetFullPath(globalExe);

        // 3. Fallback: try PATH
        try
        {
            var which = Process.Start(new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "opencode.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (which != null)
            {
                var output = which.StandardOutput.ReadToEnd().Trim();
                which.WaitForExit(2000);
                if (which.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    return output.Split(Environment.NewLine)[0].Trim();
            }
        }
        catch
        {
            // where.exe not available
        }

        return string.Empty;
    }

    /// <summary>
    /// Static version for use in FindOpencodeExecutable (no _settings access needed).
    /// Searches for opencode/ folder relative to the executable.
    /// </summary>
    private static string FindOpenCodeDirectoryStatic()
    {
        var exeDir = AppContext.BaseDirectory;

        // Search up the tree (up to 6 levels)
        var current = exeDir;
        for (int i = 0; i < 6; i++)
        {
            current = Path.GetDirectoryName(current);
            if (current == null) break;

            var candidate = Path.Combine(current, "opencode");
            if (File.Exists(Path.Combine(candidate, "opencode.json")))
                return candidate;
        }

        return Path.Combine(exeDir, "opencode");
    }
}
