using AutoApiEngine.Domain.Entities;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Logging;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// Manages saved DDL/SQL files on the server file system.
    /// Folder structure: Saved_DDL/{orgName}/{dbName}/{userFolder}/{fileName}.sql
    /// </summary>
    public class DdlFileService : IDdlFileService
    {
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IOrganizationRepository _organizationRepository;
        private readonly ILogger<DdlFileService> _logger;

        public DdlFileService(
            IWorkspaceRepository workspaceRepository,
            IOrganizationRepository organizationRepository,
            ILogger<DdlFileService> logger)
        {
            _workspaceRepository = workspaceRepository;
            _organizationRepository = organizationRepository;
            _logger = logger;
        }

        private static string RootPath => Path.Combine(AppContext.BaseDirectory, "Saved_DDL");

        public async Task SaveAsync(SaveDdlRequest request, CancellationToken cancellationToken = default)
        {
            var (orgName, dbName) = await ResolveOrgAndDbAsync(request.WorkspaceId, cancellationToken);

            var dir = Path.Combine(RootPath, Sanitize(orgName), Sanitize(dbName), Sanitize(request.FolderName));
            Directory.CreateDirectory(dir);

            var filePath = Path.Combine(dir, Sanitize(request.FileName));
            if (!filePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                filePath += ".sql";

            await File.WriteAllTextAsync(filePath, request.Content, cancellationToken);
            _logger.LogInformation("Saved DDL file: {Path}", filePath);
        }

        public async Task<DdlFileTreeNode> GetTreeAsync(string workspaceId, CancellationToken cancellationToken = default)
        {
            var (orgName, dbName) = await ResolveOrgAndDbAsync(workspaceId, cancellationToken);

            var root = new DdlFileTreeNode
            {
                Name = "Saved_DDL",
                Type = DdlFileNodeType.Folder,
                Children = new List<DdlFileTreeNode>()
            };

            var orgDir = Path.Combine(RootPath, Sanitize(orgName));
            if (!Directory.Exists(orgDir))
                return root;

            var orgNode = new DdlFileTreeNode
            {
                Name = orgName,
                Type = DdlFileNodeType.Folder,
                Children = new List<DdlFileTreeNode>()
            };
            root.Children.Add(orgNode);

            // Use the current database folder (not exposed as a tree level)
            var dbDir = Path.Combine(orgDir, Sanitize(dbName));
            if (!Directory.Exists(dbDir))
                return root;

            // Each subfolder of dbDir is a user-created folder → shown directly under org
            foreach (var userFolder in Directory.GetDirectories(dbDir))
            {
                var folderNode = new DdlFileTreeNode
                {
                    Name = Path.GetFileName(userFolder),
                    Type = DdlFileNodeType.Folder,
                    Path = userFolder,   // needed for rename requests
                    Children = new List<DdlFileTreeNode>()
                };

                foreach (var file in Directory.GetFiles(userFolder, "*.sql"))
                {
                    folderNode.Children.Add(new DdlFileTreeNode
                    {
                        Name = Path.GetFileName(file),
                        Type = DdlFileNodeType.File,
                        Path = file
                    });
                }

                orgNode.Children.Add(folderNode);
            }

            return root;
        }

        public async Task RenameAsync(RenameDdlRequest request, CancellationToken cancellationToken = default)
        {
            var currentPath = request.CurrentPath;
            var newName = Sanitize(request.NewName);

            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New name is required.");

            var parentDir = Path.GetDirectoryName(currentPath)
                ?? throw new ArgumentException("Cannot determine parent directory.");

            // Ensure the path is within Saved_DDL for security
            if (!currentPath.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Path is outside the Saved_DDL directory.");

            if (Directory.Exists(currentPath))
            {
                var newPath = Path.Combine(parentDir, newName);
                if (Directory.Exists(newPath))
                    throw new IOException($"A folder named '{newName}' already exists.");
                Directory.Move(currentPath, newPath);
                _logger.LogInformation("Renamed folder from {Old} to {New}", currentPath, newPath);
            }
            else if (File.Exists(currentPath))
            {
                // Preserve .sql extension
                if (!newName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                    newName += ".sql";
                var newPath = Path.Combine(parentDir, newName);
                if (File.Exists(newPath))
                    throw new IOException($"A file named '{newName}' already exists.");
                File.Move(currentPath, newPath);
                _logger.LogInformation("Renamed file from {Old} to {New}", currentPath, newPath);
            }
            else
            {
                throw new FileNotFoundException("The specified file or folder was not found.", currentPath);
            }
        }

        public async Task DeleteAsync(DeleteDdlRequest request, CancellationToken cancellationToken = default)
        {
            var path = request.FilePath;

            // Ensure the path is within Saved_DDL for security
            if (!path.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Path is outside the Saved_DDL directory.");

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                _logger.LogInformation("Deleted folder: {Path}", path);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogInformation("Deleted file: {Path}", path);
            }
            else
            {
                throw new FileNotFoundException("The specified file or folder was not found.", path);
            }
        }

        public async Task<string> ReadFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("The requested file was not found.", filePath);

            return await File.ReadAllTextAsync(filePath, cancellationToken);
        }

        private async Task<(string orgName, string dbName)> ResolveOrgAndDbAsync(string workspaceId, CancellationToken cancellationToken)
        {
            var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken)
                ?? throw new KeyNotFoundException($"Workspace '{workspaceId}' not found.");

            var org = await _organizationRepository.GetByIdAsync(workspace.OrganizationId.ToString(), cancellationToken)
                ?? throw new KeyNotFoundException($"Organization for workspace '{workspaceId}' not found.");

            return (org.Name, workspace.DatabaseName ?? "UnknownDB");
        }

        private static string Sanitize(string input)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", input.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized.Trim();
        }
    }
}
