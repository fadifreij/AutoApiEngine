using AutoApiEngine.Domain.Common;
using AutoApiEngine.Domain.Enums;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoApiEngine.Domain.Entities
{
    public class Workspace : BaseEntity
    {
        [Column(TypeName = "VARCHAR")]
        [StringLength(250)]
        [Required]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "VARCHAR")]
        [StringLength(75)]
        public string? EncryptionKey { get; set; } = string.Empty;

        [Column(TypeName = "VARCHAR")]
        [StringLength(50)]
        public string? DbUserName { get; set; }

        [Column(TypeName = "VARCHAR")]
        [StringLength(50)]
        public string? DbPassword { get; set; }

        [Column(TypeName = "VARCHAR")]
        [StringLength(50)]
        public string? DatabaseName { get; set; }

        /// <summary>Server address for external DB connections (e.g., "db.example.com:1433")</summary>
        [Column(TypeName = "VARCHAR")]
        [StringLength(200)]
        public string? ServerHost { get; set; }

        public DatabaseEngine DatabaseEngine { get; set; } = DatabaseEngine.SqlServer;

        public bool IsActive { get; set; } = true;

        public Guid OrganizationId { get; set; }

        public Organization Organization { get; set; } = null!;

        public DateTime? LastSyncAt { get; set; }
        public long? DatabaseSizeBytes { get; set; }
        public int TablesCount { get; set; }
        public int FunctionsCount { get; set; }
        public int StoredProceduresCount { get; set; }
    }
}