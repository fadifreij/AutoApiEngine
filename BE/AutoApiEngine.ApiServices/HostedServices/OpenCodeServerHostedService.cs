using System.Diagnostics;
using Microsoft.Extensions.Options;
using AutoApiEngine.ServiceAbstraction.DTO;

namespace AutoApiEngine.ApiServices.HostedServices;

/// <summary>
/// Auto-starts the local OpenCode server as a child process when the backend starts,
/// and shuts it down when the backend stops. Eliminates the need to manually run
/// <c>opencode serve --port 3000</c>.
/// </summary>
public sealed class OpenCodeServerHostedService : IHostedService, IDisposable
{
    private readonly ILogger<OpenCodeServerHostedService> _logger;
    private readonly OpencodeAiSettings _settings;
    private Process? _serverProcess;
    private readonly string _opencodeExe;
    private CancellationTokenSource? _healthCts;

    // PowerShell wrapper for executing opencode.ps1 on Windows
    private const string PowerShellExe = "powershell.exe";

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
                "Install with: npm install -g opencode-ai");
            return Task.CompletedTask;
        }

        // Extract hostname and port from settings.BaseUrl
        var uri = new Uri(_settings.BaseUrl.TrimEnd('/'));
        var hostname = uri.Host;
        var port = uri.Port;

        _logger.LogInformation(
            "Starting local OpenCode server on {Scheme}://{Host}:{Port} (model: {Model})...",
            uri.Scheme, hostname, port, _settings.Model);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _opencodeExe,
                Arguments = $"serve --port {port} --hostname {hostname}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory
            };

            // Set the password for the OpenCode server
            var password = !string.IsNullOrWhiteSpace(_settings.ApiKey)
                ? _settings.ApiKey
                : "local-dev-key";
            psi.EnvironmentVariables["OPENCODE_SERVER_PASSWORD"] = password;

            _serverProcess = new Process { StartInfo = psi };

            // Capture stdout for diagnostics
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
                return Task.CompletedTask;
            }

            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            _logger.LogInformation(
                "OpenCode server started (PID: {Pid}). Waiting for it to become healthy...",
                _serverProcess.Id);

            // Start health-check loop in the background
            _healthCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = WaitForHealthAsync(uri, _healthCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start OpenCode server process. " +
                "Verify opencode-ai is installed: npm install -g opencode-ai");
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _healthCts?.Cancel();

        if (_serverProcess is { HasExited: false })
        {
            _logger.LogInformation("Shutting down OpenCode server (PID: {Pid})...", _serverProcess.Id);

            try
            {
                // Graceful shutdown via SIGTERM-equivalent on Windows
                if (_serverProcess.CloseMainWindow())
                {
                    var exited = _serverProcess.WaitForExit(5000);
                    if (!exited)
                    {
                        _serverProcess.Kill(entireProcessTree: true);
                    }
                }
                else
                {
                    _serverProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while stopping OpenCode server.");
            }
        }

        _serverProcess?.Dispose();
        _serverProcess = null;
    }

    public void Dispose()
    {
        _healthCts?.Cancel();
        _healthCts?.Dispose();
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

    // ── Helper: find opencode executable ──

    private static string FindOpencodeExecutable()
    {
        // Check common locations
        var candidates = new[]
        {
            // npm global install
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm", "node_modules", "opencode-ai", "bin", "opencode.exe"),
            // Local node_modules
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "node_modules",
                "opencode-ai", "bin", "opencode.exe"),
            // PATH lookup
            "opencode.exe"
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
                // Ignore invalid paths
            }
        }

        // Fallback: try PATH
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
}
