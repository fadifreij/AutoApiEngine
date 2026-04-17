using AutoApiEngine.Domain.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace AutoApiEngine.Domain.Entities
{
    public class Organization : BaseEntity
    {
        [Column(TypeName = "VARCHAR")]
        [StringLength(250)]
        [Required]
        public string Name { get; set; } = string.Empty;
        [Column(TypeName = "VARCHAR")]
        [StringLength(250)]
        public string Description { get; set; } = string.Empty;

        
        public bool IsActive { get; set; } = true;


        // Foreign key
        public Guid OrganizationId { get; set; }

        // Navigation property
        public Organization Org { get; set; } = null!;
    }
}
