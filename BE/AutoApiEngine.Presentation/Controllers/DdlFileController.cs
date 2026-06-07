using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoApiEngine.Presentation.Controllers
{
    /// <summary>
    /// API for saving, browsing, and reading saved DDL/SQL files.
    /// Files are stored on the server under Saved_DDL/{orgName}/{dbName}/{userFolder}/.
    /// </summary>
    [ApiController]
    [Route("api/ddl-files")]
    [Authorize]
    public class DdlFileController : BaseController
    {
        private readonly IDdlFileService _ddlFileService;

        public DdlFileController(IDdlFileService ddlFileService)
        {
            _ddlFileService = ddlFileService;
        }

        /// <summary>
        /// Saves editor content to Saved_DDL/{orgName}/{dbName}/{folderName}/{fileName}.sql.
        /// </summary>
        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] SaveDdlRequest request, CancellationToken cancellationToken)
        {
            return await HandleRequestAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(request.WorkspaceId))
                    throw new ArgumentException("WorkspaceId is required.");
                if (string.IsNullOrWhiteSpace(request.FolderName))
                    throw new ArgumentException("Folder name is required.");
                if (string.IsNullOrWhiteSpace(request.FileName))
                    throw new ArgumentException("File name is required.");

                await _ddlFileService.SaveAsync(request, cancellationToken);
                return new { message = "File saved successfully." };
            });
        }

        /// <summary>
        /// Returns the folder/file tree for the given workspace's saved DDL files.
        /// </summary>
        [HttpGet("tree/{workspaceId:guid}")]
        public async Task<IActionResult> GetTree(string workspaceId, CancellationToken cancellationToken)
        {
            return await HandleRequestAsync(async () =>
            {
                var tree = await _ddlFileService.GetTreeAsync(workspaceId, cancellationToken);
                return tree;
            });
        }

        /// <summary>
        /// Renames a saved DDL file or folder on the server.
        /// </summary>
        [HttpPost("rename")]
        public async Task<IActionResult> Rename([FromBody] RenameDdlRequest request, CancellationToken cancellationToken)
        {
            return await HandleRequestAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(request.CurrentPath))
                    throw new ArgumentException("CurrentPath is required.");
                if (string.IsNullOrWhiteSpace(request.NewName))
                    throw new ArgumentException("NewName is required.");

                await _ddlFileService.RenameAsync(request, cancellationToken);
                return new { message = "Item renamed successfully." };
            });
        }

        /// <summary>
        /// Deletes a saved DDL file or folder from the server.
        /// </summary>
        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] DeleteDdlRequest request, CancellationToken cancellationToken)
        {
            return await HandleRequestAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(request.FilePath))
                    throw new ArgumentException("FilePath is required.");

                await _ddlFileService.DeleteAsync(request, cancellationToken);
                return new { message = "Item deleted successfully." };
            });
        }

        /// <summary>
        /// Reads the content of a saved file by its server path.
        /// </summary>
        [HttpGet("read")]
        public async Task<IActionResult> ReadFile([FromQuery] string path, CancellationToken cancellationToken)
        {
            return await HandleRequestAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new ArgumentException("Path is required.");

                var content = await _ddlFileService.ReadFileAsync(path, cancellationToken);
                return new { content };
            });
        }
    }
}
