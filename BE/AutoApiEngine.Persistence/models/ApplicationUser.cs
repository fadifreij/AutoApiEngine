using AutoApiEngine.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Persistence.models
{
    public class ApplicationUser : IdentityUser
    {       
        public Guid OrganizationId { get; set; }
        public Organization Organization { get; set; } = null!;
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    }
}
