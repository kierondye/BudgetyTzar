using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Infrastructure.Persistence;

public sealed class BudgetDbContext(DbContextOptions<BudgetDbContext> options) : DbContext(options)
{
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetPeriod> BudgetPeriods => Set<BudgetPeriod>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<BudgetLineAllocation> BudgetLineAllocations => Set<BudgetLineAllocation>();
    public DbSet<FinancialTransaction> Transactions => Set<FinancialTransaction>();
    public DbSet<TransactionAssignment> TransactionAssignments => Set<TransactionAssignment>();
    public DbSet<BudgetReallocation> BudgetReallocations => Set<BudgetReallocation>();
    public DbSet<BudgetAdjustment> BudgetAdjustments => Set<BudgetAdjustment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<TransactionImportBatch> TransactionImportBatches => Set<TransactionImportBatch>();
    public DbSet<TransactionImportRow> TransactionImportRows => Set<TransactionImportRow>();

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
            entity.HasIndex(x => new { x.BudgetPeriodId, x.BudgetLineId }).IsUnique();
        });

        modelBuilder.Entity<FinancialTransaction>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Description).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.SourceAccount).HasMaxLength(120);
            entity.Property(x => x.ExternalReference).HasMaxLength(160);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => new { x.BudgetId, x.TransactionDate });
            entity.HasIndex(x => x.ImportBatchId);
            entity.HasIndex(x => x.ExternalReference);
        });

        modelBuilder.Entity<TransactionAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => x.TransactionId);
            entity.HasIndex(x => x.BudgetLineId);
        });

        modelBuilder.Entity<BudgetReallocation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => x.BudgetPeriodId);
        });

        modelBuilder.Entity<BudgetAdjustment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => x.BudgetPeriodId);
            entity.HasIndex(x => x.BudgetLineId);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(4000);
            entity.HasIndex(x => new { x.BudgetId, x.BudgetPeriodId, x.OccurredAt });
            entity.HasIndex(x => new { x.BudgetId, x.AppliesToAllPeriods, x.OccurredAt });
            entity.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        modelBuilder.Entity<TransactionImportBatch>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(x => new { x.BudgetId, x.CreatedAt });
        });

        modelBuilder.Entity<TransactionImportRow>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Description).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.SourceAccount).HasMaxLength(120);
            entity.Property(x => x.ExternalReference).HasMaxLength(160);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.Property(x => x.DuplicateReason).HasMaxLength(500);
            entity.HasIndex(x => x.ImportBatchId);
            entity.HasIndex(x => x.TransactionId);
        });
    }
}
