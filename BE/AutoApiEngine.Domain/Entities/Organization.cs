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

        // 🔐 Registration code (like invite / license key)
        [Required]
        [StringLength(100)]
        public string RegistrationCode { get; set; } = string.Empty;

        // 📅 Trial & subscription
        public DateTime? TrialEndsAt { get; set; }

        public DateTime? SubscriptionEndsAt { get; set; }

        public bool IsTrial => TrialEndsAt.HasValue && DateTime.UtcNow <= TrialEndsAt;

        public bool IsSubscriptionActive =>
            SubscriptionEndsAt.HasValue && DateTime.UtcNow <= SubscriptionEndsAt;

        public bool IsExpired =>
            (!IsTrial && !IsSubscriptionActive);

        public Guid? CurrentSubscriptionId { get; set; }
        public Subscription? CurrentSubscription { get; set; }

        // Relationships
        public ICollection<Workspace> Workspaces { get; set; } = new List<Workspace>();
    }
}
