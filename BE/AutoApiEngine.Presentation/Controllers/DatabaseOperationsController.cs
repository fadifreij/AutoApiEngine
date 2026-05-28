using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.Presentation.HubServices;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.Services.DatabaseManagementServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace AutoApiEngine.Presentation.Controllers
{
    [ApiController]
    [Route("api/database")]
    [Authorize]
    public class DatabaseOperationsController : ControllerBase
    {
        
        private readonly IDatabaseManagementService _databaseManagementService;
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<ProgressHub> _hub;
        private readonly IConfiguration _configuration;
        private readonly IZipService _zipService;
        private readonly ILogger<DatabaseOperationsController> _logger;

        public DatabaseOperationsController(
        IDatabaseManagementService databaseManagementService,
        IWorkspaceRepository workspaceRepository,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IHubContext<ProgressHub> hub,
        IZipService zipService,
        ILogger<DatabaseOperationsController> logger)
        {
            _workspaceRepository = workspaceRepository;
            _databaseManagementService = databaseManagementService;
            _serviceProvider = serviceProvider;
            _hub = hub;
            _configuration = configuration;
            _zipService = zipService;
            _logger = logger;
        }

        private string BuildConnectionString(DatabaseEngine engine, string database, string? username, string? password)
        {
            IConnectionStringBuilder builder = engine switch
            {
                DatabaseEngine.MySql => _serviceProvider.GetRequiredService<MySqlConnectionStringBuilder>(),
                _ => _serviceProvider.GetRequiredService<SqlServerConnectionStringBuilder>()
            };

            var serverName = engine switch
            {
                DatabaseEngine.MySql => _configuration["MySqlServerName"] ?? "localhost",
                _ => _configuration["DatabaseServerName"] ?? "localhost"
            };

            return builder.Build(serverName, database, username, password);
        }

        [HttpPost("backup")]
        [AllowAnonymous]
        public async Task<IActionResult> Backup([FromBody] BackupRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.WorkspaceId))
                return BadRequest("Database should be selected.");
            
            var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var progress = CreateProgressReporter("backup", userId);

            var workspace = await _workspaceRepository.GetByIdWithOrganizationAsync(request.WorkspaceId, cancellationToken);

            var engine = workspace.DatabaseEngine;
            var databaseName = workspace.DatabaseName ?? "";

            var backupDir = GetTempPath("backups");

            // Build nested folder: backups/{OrganizationName}/{WorkspaceName}/
            string orgName = !string.IsNullOrWhiteSpace(workspace.Organization?.Name)
                ? workspace.Organization.Name
                : workspace.OrganizationId.ToString();
            string workspaceName = workspace.Name ?? workspace.DatabaseName ?? "workspace";

            string safeOrg = MakeSafeFolderName(orgName);
            string safeWorkspace = MakeSafeFolderName(workspaceName);

            var workspaceFolder = Path.Combine(backupDir, safeOrg, safeWorkspace);
            Directory.CreateDirectory(workspaceFolder);

            var fileName = $"{workspace.DatabaseName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak";
            var backupPath = Path.Combine(workspaceFolder, fileName);

            try
            {
                await _databaseManagementService.BackupAsync(engine, backupPath, databaseName, progress, cancellationToken);

                if (!string.IsNullOrWhiteSpace(userId))
                {
                    await _hub.Clients.Group($"user-{userId}").SendAsync("ReceiveProgress", new
                    {
                        operation = "backup",
                        percentage = 100,
                        message = "Backup completed",
                        fileName = fileName
                    }, cancellationToken);
                }

                return Ok(new { message = "Backup completed.", fileName, workspaceId = request.WorkspaceId });
            }
            catch (Exception ex)
            {
                await _hub.Clients.All.SendAsync("ReceiveProgress", new
                {
                    operation = "backup",
                    percentage = -1,
                    message = $"Backup failed: {ex.Message}",
                    fileName = (string?)null
                }, cancellationToken);

                return StatusCode(500, new { message = $"Backup failed: {ex.Message}" });
            }
        }

        [HttpGet("download/{fileName}")]
        [AllowAnonymous]
        public async Task<IActionResult> Download(string fileName, [FromQuery] string? workspaceId = null)
        {
            // Sanitize to prevent path traversal
            var safeName = Path.GetFileName(fileName);
            var backupDir = GetTempPath("backups");

            // Search recursively in backups folder for the requested file name
            try
            {
                var matches = Directory.GetFiles(backupDir, safeName, SearchOption.AllDirectories);
                if (matches == null || matches.Length == 0)
                    return NotFound(new { message = "Backup file not found." });

                var bakFilePath = matches[0];
                var fileInfo = new FileInfo(bakFilePath);
                if (fileInfo.Length == 0)
                    return NotFound(new { message = "Backup file is empty or still in progress." });

                // Zip with password using workspace's EncryptionKey from the database
                if (!string.IsNullOrWhiteSpace(workspaceId))
                {
                    var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, CancellationToken.None);
                    if (workspace != null && !string.IsNullOrWhiteSpace(workspace.EncryptionKey))
                    {
                        // Create password-protected zip using workspace's EncryptionKey
                        var downloadZipPath = Path.Combine(
                            Path.GetDirectoryName(bakFilePath) ?? ".",
                            $"{Path.GetFileNameWithoutExtension(safeName)}_download_{DateTime.UtcNow:yyyyMMddHHmmss}.zip");

                        await _zipService.ZipWithPasswordAsync(bakFilePath, workspace.EncryptionKey, downloadZipPath);

                        var zipName = Path.GetFileNameWithoutExtension(safeName) + ".zip";
                        var zipFileInfo = new FileInfo(downloadZipPath);
                        // FileOptions.DeleteOnClose ensures the download zip is deleted after transfer
                        // The original .bak file is preserved for history
                        var zipStream = new FileStream(downloadZipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
                        
                        // Set Content-Length header for download progress tracking
                        Response.Headers.ContentLength = zipFileInfo.Length;
                        return File(zipStream, "application/zip", zipName, enableRangeProcessing: true);
                    }
                }

                // No workspace provided - create unprotected zip for download
                var unprotectedZipPath = Path.Combine(
                    Path.GetDirectoryName(bakFilePath) ?? ".",
                    $"{Path.GetFileNameWithoutExtension(safeName)}_download_{DateTime.UtcNow:yyyyMMddHHmmss}.zip");

                await _zipService.ZipAsync(bakFilePath, unprotectedZipPath);

                var unprotectedZipInfo = new FileInfo(unprotectedZipPath);
                var unprotectedStream = new FileStream(unprotectedZipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
                var unprotectedZipName = Path.GetFileNameWithoutExtension(safeName) + ".zip";
                
                // Set Content-Length header for download progress tracking
                Response.Headers.ContentLength = unprotectedZipInfo.Length;
                return File(unprotectedStream, "application/zip", unprotectedZipName, enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                _ = ex; // swallow for now but could log
                return NotFound(new { message = "Backup file not found." });
            }
        }

        [HttpGet("last-backup/{workspaceId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetLastBackup(string workspaceId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
                return BadRequest(new { message = "Workspace id is required." });

            var workspace = await _workspaceRepository.GetByIdWithOrganizationAsync(workspaceId, cancellationToken);
            if (workspace == null)
                return NotFound(new { message = "Workspace not found." });

            var backupDir = GetTempPath("backups");

            string orgName = !string.IsNullOrWhiteSpace(workspace.Organization?.Name)
                ? workspace.Organization.Name
                : workspace.OrganizationId.ToString();
            string workspaceName = workspace.Name ?? workspace.DatabaseName ?? "workspace";

            string safeOrg = MakeSafeFolderName(orgName);
            string safeWorkspace = MakeSafeFolderName(workspaceName);

            var workspaceFolder = Path.Combine(backupDir, safeOrg, safeWorkspace);
            if (!Directory.Exists(workspaceFolder))
                return NotFound(new { message = "No backups found." });

            var files = Directory.GetFiles(workspaceFolder);
            if (files == null || files.Length == 0)
                return NotFound(new { message = "No backups found." });

            var latest = files
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest == null)
                return NotFound(new { message = "No backups found." });

            return Ok(new
            {
                fileName = latest.Name,
                size = latest.Length,
                createdAt = latest.LastWriteTimeUtc
            });
        }


        [HttpPost("restore")]
        [AllowAnonymous]
        public async Task<IActionResult> Restore([FromForm] RestoreRequest request, IFormFile file, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.WorkspaceId))
                return BadRequest("Database should be selected.");

            if (file == null || file.Length == 0)
                return BadRequest("Backup file is required.");

            // Validate file extension - only .bak or .zip allowed
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (fileExtension != ".bak" && fileExtension != ".zip")
                return BadRequest("Only .bak or .zip files are allowed for restore.");

            var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var uploadProgress = CreateProgressReporter("upload", userId);
            var restoreProgress = CreateProgressReporter("restore", userId);

            var tempPath = GetTempPath("restore");
            var filePath = Path.Combine(tempPath, file.FileName);

            // Upload with progress
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                using var inputStream = file.OpenReadStream();

                while ((read = await inputStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await stream.WriteAsync(buffer, 0, read, cancellationToken);

                    totalRead += read;
                    int percentage = (int)((totalRead * 100) / file.Length);

                    uploadProgress.Report(new DatabaseProgress
                    {
                        Percentage = percentage,
                        Message = $"Uploading... {percentage}%"
                    });
                }
            }

            // Get workspace to retrieve encryption key for password-protected zips
            var workspace = await _workspaceRepository.GetByIdAsync(request.WorkspaceId, cancellationToken);
            if (workspace == null)
                return NotFound("Workspace not found.");
            string restorePath = filePath;
            if (fileExtension == ".zip")
            {
                try
                {
                    var extractDir = Path.Combine(tempPath, Path.GetFileNameWithoutExtension(file.FileName));

                    if (_zipService.IsPasswordProtected(filePath))
                    {
                        if (string.IsNullOrWhiteSpace(workspace.EncryptionKey))
                            return BadRequest("The uploaded zip is password-protected but no encryption key is configured for this workspace.");

                        restorePath = await _zipService.UnzipWithPasswordAsync(filePath, workspace.EncryptionKey, extractDir, cancellationToken);
                    }
                    else
                    {
                        restorePath = await _zipService.UnzipAsync(filePath, extractDir, cancellationToken);
                    }

                    // Validate that the extracted file is a .bak file
                    if (!Path.GetExtension(restorePath).Equals(".bak", StringComparison.OrdinalIgnoreCase))
                    {
                        // Look for .bak file in extracted directory
                        var bakFiles = Directory.GetFiles(extractDir, "*.bak", SearchOption.AllDirectories);
                        if (bakFiles.Length == 0)
                            return BadRequest("No valid .bak backup file found inside the zip archive.");
                        
                        restorePath = bakFiles[0];
                    }
                }
                catch (Exception ex)
                {
                    return BadRequest($"Failed to extract zip file: {ex.Message}");
                }
            }


            var engine = workspace.DatabaseEngine;
            // Use workspace-specific credentials, or fall back to default config credentials
            string connectionString;
            if (!string.IsNullOrWhiteSpace(workspace.DbUserName))
            {
                connectionString = BuildConnectionString(engine, workspace.DatabaseName ?? "", workspace.DbUserName, workspace.DbPassword);
            }
            else
            {
                // No dedicated credentials — use the default connection string from config based on engine type
                if (engine == DatabaseEngine.MySql)
                {
                    connectionString = _configuration.GetConnectionString("MySqlConnection")
                        ?? $"Server=localhost;Port=3307;Uid=root;Pwd=root;Database={workspace.DatabaseName}";
                    if (!connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase))
                        connectionString += $";Database={workspace.DatabaseName}";
                }
                else
                {
                    connectionString = _configuration.GetConnectionString("SqlServerConnection")
                        ?? $"Server=localhost;Database={workspace.DatabaseName};Trusted_Connection=True;TrustServerCertificate=True";
                    // For SQL Server, ensure the database name is in the connection string
                    var csb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
                    {
                        InitialCatalog = workspace.DatabaseName ?? ""
                    };
                    connectionString = csb.ConnectionString;
                }
            }

            DatabaseOptions databaseOptions = new DatabaseOptions
            {
                ConnectionString = connectionString,
                DatabaseName = workspace.DatabaseName ?? ""
            };

            try
            {
                _logger.LogInformation("Starting restore for workspace {WorkspaceId}, database {DatabaseName}", request.WorkspaceId, workspace.DatabaseName);
                
                await _databaseManagementService.RestoreAsync(
                    engine,
                    restorePath,
                    databaseOptions,
                    restoreProgress,
                    cancellationToken);

                _logger.LogInformation("Restore completed for workspace {WorkspaceId}, sending 100% progress to user {UserId}", request.WorkspaceId, userId);

                // Send final 100% progress via SignalR to ensure UI shows completion
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    await _hub.Clients.Group($"user-{userId}").SendAsync("ReceiveProgress", new
                    {
                        operation = "restore",
                        percentage = 100,
                        message = "Restore completed"
                    }, cancellationToken);
                    
                    // Small delay to ensure SignalR message is sent before HTTP response
                    await Task.Delay(100, cancellationToken);
                }

                _logger.LogInformation("Restore fully completed for workspace {WorkspaceId}", request.WorkspaceId);
                return Ok(new { message = "Restore completed." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed for workspace {WorkspaceId}", request.WorkspaceId);
                
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    await _hub.Clients.Group($"user-{userId}").SendAsync("ReceiveProgress", new
                    {
                        operation = "restore",
                        percentage = -1,
                        message = $"Restore failed: {ex.Message}"
                    }, cancellationToken);
                }

                return StatusCode(500, new { message = $"Restore failed: {ex.Message}" });
            }
        }

        private IProgress<DatabaseProgress> CreateProgressReporter(string operationType, string? userId = null)
        {
            // Use a direct callback instead of Progress<T> which relies on SynchronizationContext
            return new DirectProgress<DatabaseProgress>(async p =>
            {
                // Use lowercase property names for SignalR to match frontend expectations
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    await _hub.Clients.Group($"user-{userId}").SendAsync("ReceiveProgress", new
                    {
                        operation = operationType,
                        percentage = p.Percentage,
                        message = p.Message
                    });
                }
                else
                {
                    await _hub.Clients.All.SendAsync("ReceiveProgress", new
                    {
                        operation = operationType,
                        percentage = p.Percentage,
                        message = p.Message
                    });
                }
            });
        }

        /// <summary>
        /// IProgress implementation that invokes the callback directly on the reporting thread,
        /// avoiding SynchronizationContext issues in fire-and-forget background tasks.
        /// </summary>
        private sealed class DirectProgress<T> : IProgress<T>
        {
            private readonly Func<T, Task> _handler;
            public DirectProgress(Func<T, Task> handler) => _handler = handler;
            public void Report(T value) => _handler(value).GetAwaiter().GetResult();
        }
        private string GetTempPath(string pathName)
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string tempFolder = Path.Combine(basePath, pathName);

            Directory.CreateDirectory(tempFolder);
            return tempFolder;
        }

        private static string MakeSafeFolderName(string input)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                input = input.Replace(c, '_');
            return input.Trim();
        }
    }
}

