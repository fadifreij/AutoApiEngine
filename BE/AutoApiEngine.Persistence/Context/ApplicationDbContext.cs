using AutoApiEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using static System.Collections.Specialized.BitVector32;

namespace AutoApiEngine.Persistence.Context
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
        {
        }

        public DbSet<Organization> Organizations { get; set; } = null!;
        public DbSet<Workspace> Workspaces { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Organization>()
                .HasOne(o => o.Org)
                .WithMany() // If you want, you can add a collection like "SubOrganizations"
                .HasForeignKey(o => o.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict); // NO ACTION
        }
    }
}
