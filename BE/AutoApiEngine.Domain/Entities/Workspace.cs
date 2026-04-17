using AutoApiEngine.Domain.Common;
using AutoApiEngine.Domain.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

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
        public string ApiKey { get; set; } = string.Empty;
        [Column(TypeName = "VARCHAR")]
        [StringLength(50)]
        public string? DbUserName{ get; set; }
        [Column(TypeName = "VARCHAR")]
        [StringLength(50)]
        public string? DbPassword { get; set; }

        [Column(TypeName = "VARCHAR")]
        [StringLength(50)]
        public string? DatabaseName { get; set; }
        public DatabaseEngine DatabaseEngine { get; set; } = DatabaseEngine.SqlServer;
       
        public bool IsActive { get; set; } = true;
        // Foreign key public
        Guid OrganizationId { get; set; } 
        //Navigation property
        public Organization Org { get; set; } = null!;
    }
}
