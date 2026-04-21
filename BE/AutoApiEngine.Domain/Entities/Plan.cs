using AutoApiEngine.Domain.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace AutoApiEngine.Domain.Entities
{
    public class Plan : BaseEntity
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty; // Free, Basic, Pro, Enterprise

        public decimal Price { get; set; }

        public int DurationInDays { get; set; } // e.g. 30, 365

        // Feature limits
        public int MaxWorkspaces { get; set; }
        public int MaxUsers { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
