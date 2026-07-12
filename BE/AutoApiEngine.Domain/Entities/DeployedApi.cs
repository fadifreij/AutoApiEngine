using AutoApiEngine.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoApiEngine.Domain.Entities
{
    /// <summary>
    /// Represents a deployed (saved) dynamic API configuration.
    /// Stores the workspace, target object, column selection, filters, and sorts
    /// so the API can be re-invoked later without reconfiguring.
    /// </summary>
    public class DeployedApi : BaseEntity
    {
        /// <summary>Workspace this API belongs to.</summary>
        [Required]
        public Guid WorkspaceId { get; set; }

        /// <summary>Navigation to the workspace.</summary>
        public Workspace Workspace { get; set; } = null!;

        /// <summary>User-friendly name (auto-generated from object name).</summary>
        [Column(TypeName = "VARCHAR")]
        [StringLength(250)]
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>The database object name (table / view / sp / function).</summary>
        [Column(TypeName = "VARCHAR")]
        [StringLength(250)]
        [Required]
        public string ObjectName { get; set; } = string.Empty;

        /// <summary>JSON-serialized list of selected columns (dot-notation for related).</summary>
        public string? SelectColumns { get; set; }

        /// <summary>JSON-serialized list of FilterConfig.</summary>
        public string? Filters { get; set; }

        /// <summary>JSON-serialized list of SortConfig.</summary>
        public string? Sorts { get; set; }

        /// <summary>Default page size.</summary>
        public int PageSize { get; set; } = 100;

        /// <summary>Whether this deployed API is active.</summary>
        public bool IsActive { get; set; } = true;
    }
}
