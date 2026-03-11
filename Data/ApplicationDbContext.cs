using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Vizora.Models;
using Vizora.Enums;

namespace Vizora.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Category> Categories { get; set; }

        public DbSet<Transaction> Transactions { get; set; }

        public DbSet<BudgetPeriod> BudgetPeriods { get; set; }

        public DbSet<Budget> Budgets { get; set; }

        public DbSet<AuditLog> AuditLogs { get; set; }

        public DbSet<RecurringTransaction> RecurringTransactions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Categories are user-owned and unique by name/type per user.
            modelBuilder.Entity<Category>(entity =>
            {
                entity.Property(c => c.UserId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(c => c.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(c => c.Type)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                entity.Property(c => c.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(c => c.User)
                    .WithMany(u => u.Categories)
                    .HasForeignKey(c => c.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(c => new { c.UserId, c.Name, c.Type })
                    .IsUnique();
            });

            // Transactions are user-owned and linked to a user-owned category.
            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.Property(t => t.UserId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(t => t.Amount)
                    .HasPrecision(18, 2);

                entity.Property(t => t.Type)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                entity.Property(t => t.Description)
                    .HasMaxLength(250);

                entity.Property(t => t.TransactionDate)
                    .IsRequired();

                entity.Property(t => t.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(t => t.UpdatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(t => t.Category)
                    .WithMany(c => c.Transactions)
                    .HasForeignKey(t => t.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(t => t.User)
                    .WithMany(u => u.Transactions)
                    .HasForeignKey(t => t.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(t => new { t.UserId, t.TransactionDate });
                entity.HasIndex(t => new { t.UserId, t.Type });
                entity.HasIndex(t => new { t.UserId, t.CategoryId });
            });

            // Budget periods are user-owned ranges used by one or more category budgets.
            modelBuilder.Entity<BudgetPeriod>(entity =>
            {
                entity.Property(bp => bp.UserId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(bp => bp.Type)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                entity.Property(bp => bp.StartDate)
                    .IsRequired();

                entity.Property(bp => bp.EndDate)
                    .IsRequired();

                entity.Property(bp => bp.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(bp => bp.User)
                    .WithMany(u => u.BudgetPeriods)
                    .HasForeignKey(bp => bp.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(bp => new { bp.UserId, bp.Type, bp.StartDate, bp.EndDate })
                    .IsUnique();

                entity.ToTable(t =>
                    t.HasCheckConstraint("CK_BudgetPeriods_DateRange", "\"EndDate\" >= \"StartDate\""));
            });

            // Budgets are user-owned spending plans tied to category and period.
            modelBuilder.Entity<Budget>(entity =>
            {
                entity.Property(b => b.UserId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(b => b.PlannedAmount)
                    .HasPrecision(18, 2);

                entity.Property(b => b.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(b => b.UpdatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(b => b.User)
                    .WithMany(u => u.Budgets)
                    .HasForeignKey(b => b.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(b => b.Category)
                    .WithMany(c => c.Budgets)
                    .HasForeignKey(b => b.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(b => b.BudgetPeriod)
                    .WithMany(bp => bp.Budgets)
                    .HasForeignKey(b => b.BudgetPeriodId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(b => new { b.UserId, b.CategoryId, b.BudgetPeriodId })
                    .IsUnique();
                entity.HasIndex(b => new { b.UserId, b.BudgetPeriodId });

                entity.ToTable(t =>
                    t.HasCheckConstraint("CK_Budgets_PlannedAmount_Positive", "\"PlannedAmount\" > 0"));
            });

            // Recurring transactions are user-owned templates that generate normal transactions on schedule.
            modelBuilder.Entity<RecurringTransaction>(entity =>
            {
                entity.Property(rt => rt.UserId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(rt => rt.Amount)
                    .HasPrecision(18, 2);

                entity.Property(rt => rt.Type)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                entity.Property(rt => rt.Frequency)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                entity.Property(rt => rt.Description)
                    .HasMaxLength(250);

                entity.Property(rt => rt.StartDate)
                    .IsRequired();

                entity.Property(rt => rt.NextRunDate)
                    .IsRequired();

                entity.Property(rt => rt.IsActive)
                    .HasDefaultValue(true);

                entity.Property(rt => rt.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(rt => rt.User)
                    .WithMany(u => u.RecurringTransactions)
                    .HasForeignKey(rt => rt.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(rt => rt.Category)
                    .WithMany(c => c.RecurringTransactions)
                    .HasForeignKey(rt => rt.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(rt => new { rt.UserId, rt.IsActive, rt.NextRunDate });
                entity.HasIndex(rt => new { rt.UserId, rt.CategoryId });

                entity.ToTable(t =>
                    t.HasCheckConstraint("CK_RecurringTransactions_Amount_Positive", "\"Amount\" > 0"));
            });

            // Audit logs capture user-scoped financial change and export events.
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.Property(a => a.EventType)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(a => a.EntityType)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(a => a.EntityId)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(a => a.UserId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(a => a.IpAddress)
                    .HasMaxLength(64);

                entity.Property(a => a.OldValues)
                    .HasColumnType("text");

                entity.Property(a => a.NewValues)
                    .HasColumnType("text");

                entity.Property(a => a.Timestamp)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne<ApplicationUser>()
                    .WithMany()
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(a => new { a.UserId, a.Timestamp });
                entity.HasIndex(a => new { a.EntityType, a.EntityId, a.Timestamp });
            });

            // Track account creation timestamp for auditability.
            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(u => u.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");
            });
        }
    }
}
