using System.Text.Json.Serialization;

namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// A node in the saved-DDL file tree.
    /// Folders contain children; files have a server file path that can be read.
    /// </summary>
    public class DdlFileTreeNode
    {
        public string Name { get; set; } = string.Empty;
        public DdlFileNodeType Type { get; set; }

        /// <summary>Full server path (used for reading file content). Only set for files.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Path { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<DdlFileTreeNode>? Children { get; set; }
    }

    public enum DdlFileNodeType
    {
        Folder,
        File
    }
}
