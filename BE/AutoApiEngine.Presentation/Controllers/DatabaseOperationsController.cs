using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Domain.Enums;
using AutoApiEngine.Presentation.HubServices;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.Services.DatabaseManagementServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Collections.Generic;
using System.Text;

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

        public DatabaseOperationsController(
        IDatabaseManagementService databaseManagementService,
        IWorkspaceRepository workspaceRepository,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IHubContext<ProgressHub> hub)
        {
            _workspaceRepository = workspaceRepository;
            _databaseManagementService = databaseManagementService;
            _serviceProvider = serviceProvider;
            _hub = hub;
            _configuration = configuration;
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
                        Operation = "backup",
                        Percentage = 100,
                        Message = "Backup completed",
                        FileName = fileName
                    }, cancellationToken);
                }

                return Ok(new { message = "Backup completed.", fileName });
            }
            catch (Exception ex)
            {
                await _hub.Clients.All.SendAsync("ReceiveProgress", new
                {
                    Operation = "backup",
                    Percentage = -1,
                    Message = $"Backup failed: {ex.Message}",
                    FileName = (string?)null
                }, cancellationToken);

                return StatusCode(500, new { message = $"Backup failed: {ex.Message}" });
            }
        }

        [HttpGet("download/{fileName}")]
        [AllowAnonymous]
        public IActionResult Download(string fileName)
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

                var filePath = matches[0];
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                    return NotFound(new { message = "Backup file is empty or still in progress." });

                var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                return File(stream, "application/octet-stream", safeName);
            }
            catch (Exception ex)
            {
                _ = ex; // swallow for now but could log
                return NotFound(new { message = "Backup file not found." });
            }
        }


        [HttpPost("restore")]
        [AllowAnonymous]
        public async Task<IActionResult> Restore([FromForm] RestoreRequest request, IFormFile file, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.WorkspaceId))
                return BadRequest("Database should be selected.");

            if (file == null || file.Length == 0)
                return BadRequest("Backup file is required.");

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

            var workspace = await _workspaceRepository.GetByIdAsync(request.WorkspaceId, cancellationToken);

            var engine = workspace.DatabaseEngine;

            // Use workspace-specific credentials, or fall back to default config credentials
            string connectionString;
            if (!string.IsNullOrWhiteSpace(workspace.DbUserName))
            {
                connectionString = BuildConnectionString(engine, workspace.DatabaseName ?? "", workspace.DbUserName, workspace.DbPassword);
            }
            else
            {
                // No dedicated credentials — use the default connection string from config
                connectionString = _configuration.GetConnectionString("MySqlConnection")
                    ?? $"Server=localhost;Port=3307;Uid=root;Pwd=root;Database={workspace.DatabaseName}";
                if (!connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase))
                    connectionString += $";Database={workspace.DatabaseName}";
            }

            DatabaseOptions databaseOptions = new DatabaseOptions
            {
                ConnectionString = connectionString,
                DatabaseName = workspace.DatabaseName ?? ""
            };

            try
            {
                await _databaseManagementService.RestoreAsync(
                    engine,
                    filePath,
                    databaseOptions,
                    restoreProgress,
                    cancellationToken);

                return Ok(new { message = "Restore completed." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Restore failed: {ex.Message}" });
            }
        }

        private IProgress<DatabaseProgress> CreateProgressReporter(string operation, string? userId = null)
        {
            // Use a direct callback instead of Progress<T> which relies on SynchronizationContext
            return new DirectProgress<DatabaseProgress>(async p =>
            {
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    await _hub.Clients.Group($"user-{userId}").SendAsync("ReceiveProgress", new
                    {
                        Operation = operation,
                        p.Percentage,
                        p.Message
                    });
                }
                else
                {
                    await _hub.Clients.All.SendAsync("ReceiveProgress", new
                    {
                        Operation = operation,
                        p.Percentage,
                        p.Message
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