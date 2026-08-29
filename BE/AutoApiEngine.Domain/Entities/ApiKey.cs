using AutoApiEngine.Domain.Common;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoApiEngine.Domain.Entities
{
    public class ApiKey : BaseEntity
    {
        [Column(TypeName = "VARCHAR")]
        [StringLength(250)]
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>SHA-256 hex hash of the key. The raw value is never stored.</summary>
        [Required]
        [StringLength(500)]
        public string Key { get; set; } = string.Empty;

        public DateTime? ExpiresAt { get; set; }

        public DateTime? LastUsedAt { get; set; }

        public bool IsActive { get; set; } = true;

        public Guid OrganizationId { get; set; }

        public Organization Organization { get; set; } = null!;
    }
}