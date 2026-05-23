using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Presentation.HubServices;
using AutoApiEngine.ServiceAbstraction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Presentation.Controllers
{
    [ApiController]
    [Route("api/database")]
    public class DatabaseOperationsController : ControllerBase
    {
        
        private readonly IDatabaseManagementService _databaseManagementService;
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IConnectionStringBuilder _connectionStringBuilder;
        private readonly IHubContext<ProgressHub> _hub;
        private readonly string _serverName;

        public DatabaseOperationsController(
        IDatabaseManagementService databaseManagementService,
        IWorkspaceRepository workspaceRepository,
        IConnectionStringBuilder connectionStringBuilder,
        String ServerName,
        IHubContext<ProgressHub> hub)
        {
            _workspaceRepository = workspaceRepository;
            _databaseManagementService= databaseManagementService;
            _connectionStringBuilder = connectionStringBuilder;
            _hub = hub;
            _serverName = ServerName;
        }

        [HttpPost("backup")]
        public async Task<IActionResult> Backup([FromBody] BackupRequest request,CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.WorkspaceId))
                return BadRequest("Database should be selected.");

            var progress = CreateProgressReporter("backup");

            var workspace = await _workspaceRepository.GetByIdAsync(request.WorkspaceId,cancellationToken);
             
            DatabaseOptions databaseOptions = new DatabaseOptions
            {
                ConnectionString = _connectionStringBuilder.Build(_serverName,workspace.DatabaseName??"",workspace.DbUserName,workspace.DbPassword),
                DatabaseName = workspace.DatabaseName?? ""
            };

            var engine = workspace.DatabaseEngine;
            _ = Task.Run(() =>
                _databaseManagementService.BackupAsync(engine, GetTempPath("temp"), databaseOptions, progress));

            return Accepted(new { message = "Backup started." });
        }


        [HttpPost("restore")]
        public async Task<IActionResult> Restore([FromForm] RestoreRequest request,IFormFile file, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.WorkspaceId))
                return BadRequest("Database should be selected.");

            if (file == null || file.Length == 0)
                return BadRequest("Backup file is required.");

            var uploadProgress = CreateProgressReporter("upload");
            var restoreProgress = CreateProgressReporter("restore");

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

            DatabaseOptions databaseOptions = new DatabaseOptions
            {
                ConnectionString = _connectionStringBuilder.Build(
                    _serverName,
                    workspace.DatabaseName ?? "",
                    workspace.DbUserName,
                    workspace.DbPassword),
                DatabaseName = workspace.DatabaseName ?? ""
            };

            var engine = workspace.DatabaseEngine;
            _ = Task.Run(() =>
                _databaseManagementService.RestoreAsync(
                    engine,
                    filePath,
                    databaseOptions,
                    restoreProgress));

            return Accepted(new { message = "Restore started." });
        }

        private IProgress<DatabaseProgress> CreateProgressReporter(string operation)
        {
            return new Progress<DatabaseProgress>(async progress =>
            {
                await _hub.Clients.All.SendAsync("ReceiveProgress", new
                {
                    Operation = operation,
                    progress.Percentage,
                    progress.Message
                });
            });
        }
        private string GetTempPath(string pathName)
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string tempFolder = Path.Combine(basePath, pathName);

            Directory.CreateDirectory(tempFolder);
            return tempFolder;
        }
    }
}
