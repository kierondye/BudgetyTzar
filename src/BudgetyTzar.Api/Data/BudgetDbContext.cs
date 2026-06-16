using BudgetyTzar.Api;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Data;

public sealed class BudgetDbContext(DbContextOptions<BudgetDbContext> options) : DbContext(options)
{
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetPeriod> BudgetPeriods => Set<BudgetPeriod>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<BudgetLineAllocation> BudgetLineAllocations => Set<BudgetLineAllocation>();
    public DbSet<FinancialTransaction> Transactions => Set<FinancialTransaction>();
    public DbSet<TransactionAssignment> TransactionAssignments => Set<TransactionAssignment>();
    public DbSet<BudgetReallocation> BudgetReallocations => Set<BudgetReallocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<BudgetPeriod>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => new { x.BudgetId, x.StartDate, x.EndDate }).IsUnique();
        });

        modelBuilder.Entity<BudgetLine>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.RolloverType).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.BudgetId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<BudgetLineAllocation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => new { x.BudgetPeriodId, x.BudgetLineId }).IsUnique();
        });

        modelBuilder.Entity<FinancialTransaction>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Description).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.SourceAccount).HasMaxLength(120);
            entity.Property(x => x.ExternalReference).HasMaxLength(160);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => new { x.BudgetId, x.TransactionDate });
            entity.HasIndex(x => x.ExternalReference);
        });

        modelBuilder.Entity<TransactionAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => x.TransactionId);
            entity.HasIndex(x => x.BudgetLineId);
        });

        modelBuilder.Entity<BudgetReallocation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => x.BudgetPeriodId);
        });
    }
}
