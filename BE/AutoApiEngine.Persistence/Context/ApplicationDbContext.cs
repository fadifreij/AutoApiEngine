using AutoApiEngine.Domain.Common;
using AutoApiEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;


namespace AutoApiEngine.Persistence.Context
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SetAuditTimestamps();
            return base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            SetAuditTimestamps();
            return base.SaveChanges();
        }

        private void SetAuditTimestamps()
        {
            var entries = ChangeTracker
                .Entries<BaseEntity>()
                .Where(e => e.State == EntityState.Added);

            foreach (var entry in entries)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
            }
        }

        public DbSet<Organization> Organizations { get; set; } = null!;
        public DbSet<Workspace> Workspaces { get; set; } = null!;
        public DbSet<DeployedApi> DeployedApis { get; set; } = null!;

        public DbSet<Plan> Plans { get; set; } = null!;
        public DbSet<Subscription> Subscriptions { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Subscription>()
                        .HasOne(s => s.Plan)
                        .WithMany()
                        .HasForeignKey(s => s.PlanId);

            modelBuilder.Entity<Subscription>()
                .HasOne(s => s.Organization)
                .WithMany()
                .HasForeignKey(s => s.OrganizationId);

            modelBuilder.Entity<Organization>()
                .HasOne(o => o.CurrentSubscription)
                .WithMany() // 🔴 important: no back navigation
                .HasForeignKey(o => o.CurrentSubscriptionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Plan>()
                .Property(p => p.Price)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Subscription>()
                .Property(s => s.AmountPaid)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Workspace>()
                .HasOne(w => w.Organization)
                .WithMany(o => o.Workspaces)
                .HasForeignKey(w => w.OrganizationId);

            modelBuilder.Entity<DeployedApi>()
                .HasOne(d => d.Workspace)
                .WithMany()
                .HasForeignKey(d => d.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
           
            modelBuilder.Entity<Plan>().HasData(
            new Plan
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Free",
                Price = 0,
                DurationInDays = 14,
                MaxWorkspaces = 1,
                MaxUsers = 1
            },
            new Plan
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Security",
                Price = 10,
                DurationInDays = 30,
                MaxWorkspaces = 3,
                MaxUsers = 5
            },
            new Plan
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Basic",
                Price = 25,
                DurationInDays = 30,
                MaxWorkspaces = 10,
                MaxUsers = 20
            },
            new Plan
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "Pro",
                Price = 100,
                DurationInDays = 30,
                MaxWorkspaces = 100,
                MaxUsers = 1000
            },
             new Plan
             {
                 Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                 Name = "Enterprise",
                 Price = 100,
                 DurationInDays = 30,
                 MaxWorkspaces = 100,
                 MaxUsers = 1000
             }
            );
        }


    }
}
