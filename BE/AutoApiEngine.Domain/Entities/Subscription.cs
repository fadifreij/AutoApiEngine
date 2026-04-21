using AutoApiEngine.Domain.Common;
using AutoApiEngine.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Domain.Entities
{
    public class Subscription : BaseEntity
    {
        public Guid OrganizationId { get; set; }
        public Organization Organization { get; set; } = null!;

        public Guid PlanId { get; set; }
        public Plan Plan { get; set; } = null!;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public decimal AmountPaid { get; set; }

        public PaymentStatus Status { get; set; } = PaymentStatus.Completed;

        public bool IsActive =>
            DateTime.UtcNow >= StartDate && DateTime.UtcNow <= EndDate;
    }
}
