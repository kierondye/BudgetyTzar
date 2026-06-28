using Microsoft.EntityFrameworkCore;
using BudgetyTzar.Api.Application.Reporting;

namespace BudgetyTzar.Api.Infrastructure.Persistence;

public sealed class BudgetDbContext(DbContextOptions<BudgetDbContext> options) : DbContext(options)
{
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetItem> BudgetItems => Set<BudgetItem>();
    public DbSet<FinancialTransaction> Transactions => Set<FinancialTransaction>();
    public DbSet<TransactionAllocation> TransactionAllocations => Set<TransactionAllocation>();
    public DbSet<BudgetReallocation> BudgetReallocations => Set<BudgetReallocation>();
    public DbSet<BudgetAdjustment> BudgetAdjustments => Set<BudgetAdjustment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<BudgetSnapshotProjection> BudgetSnapshotProjections => Set<BudgetSnapshotProjection>();
    public DbSet<BudgetSnapshotItemProjection> BudgetSnapshotItemProjections => Set<BudgetSnapshotItemProjection>();
    public DbSet<ProcessedProjectionEvent> ProcessedProjectionEvents => Set<ProcessedProjectionEvent>();
    public DbSet<BudgetItemProjectionState> BudgetItemProjectionStates => Set<BudgetItemProjectionState>();
    public DbSet<BudgetAdjustmentProjectionState> BudgetAdjustmentProjectionStates => Set<BudgetAdjustmentProjectionState>();
    public DbSet<TransactionProjectionState> TransactionProjectionStates => Set<TransactionProjectionState>();
    public DbSet<TransactionAllocationProjectionState> TransactionAllocationProjectionStates => Set<TransactionAllocationProjectionState>();
    public DbSet<ProjectionEventFailure> ProjectionEventFailures => Set<ProjectionEventFailure>();
    public DbSet<AuditEventFailure> AuditEventFailures => Set<AuditEventFailure>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<BudgetItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(x => x.ArchivedAt);
            entity.HasIndex(x => new { x.BudgetId, x.Name }).IsUnique();
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
            entity.HasIndex(x => x.ExternalReference);
        });

        modelBuilder.Entity<TransactionAllocation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => x.TransactionId);
            entity.HasIndex(x => x.BudgetItemId);
        });

        modelBuilder.Entity<BudgetReallocation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => new { x.BudgetId, x.Date });
        });

        modelBuilder.Entity<BudgetAdjustment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.LegacySignedAmount).HasPrecision(18, 2);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => x.BudgetItemId);
            entity.HasIndex(x => new { x.BudgetId, x.Date });
            entity.HasIndex(x => x.ReallocationId);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(4000);
            entity.HasIndex(x => new { x.BudgetId, x.OccurredAt });
            entity.HasIndex(x => new { x.BudgetId, x.AppliesToAllPeriods, x.OccurredAt });
            entity.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Topic).HasMaxLength(160).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(160).IsRequired();
            entity.Property(x => x.AggregateType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.EnvelopeJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.LastError).HasMaxLength(1000);
            entity.Property(x => x.PublishingLockId);
            entity.Property(x => x.PublishingLockedAt);
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
            entity.HasIndex(x => new { x.Status, x.PublishingLockedAt, x.CreatedAt });
            entity.HasIndex(x => x.PublishingLockId);
            entity.HasIndex(x => x.ProjectedAt);
            entity.HasIndex(x => new { x.BudgetId, x.CreatedAt });
            entity.HasIndex(x => x.EventType);
        });

        modelBuilder.Entity<BudgetSnapshotProjection>(entity =>
        {
            entity.ToTable("budget_snapshot");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UnbudgetedBalance).HasPrecision(18, 2);
            entity.Property(x => x.TotalBalance).HasPrecision(18, 2);
            entity.Property(x => x.TotalTransactionBalance).HasPrecision(18, 2);
            entity.Property(x => x.TotalBudgetedBalance).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BudgetId, x.Date }).IsUnique();
        });

        modelBuilder.Entity<BudgetSnapshotItemProjection>(entity =>
        {
            entity.ToTable("budget_snapshot_item");
            entity.HasKey(x => new { x.SnapshotId, x.BudgetItemId });
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Balance).HasPrecision(18, 2);
            entity.Property(x => x.PlannedCredit).HasPrecision(18, 2);
            entity.Property(x => x.PlannedDebit).HasPrecision(18, 2);
            entity.Property(x => x.ActualCredit).HasPrecision(18, 2);
            entity.Property(x => x.ActualDebit).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BudgetId, x.Date });
        });

        modelBuilder.Entity<ProcessedProjectionEvent>(entity =>
        {
            entity.ToTable("processed_projection_event");
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventType).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.LastError).HasMaxLength(4000);
            entity.HasIndex(x => new { x.BudgetId, x.ProcessedAt });
            entity.HasIndex(x => new { x.Status, x.ProcessingUpdatedAt });
            entity.HasIndex(x => x.ProcessingInstanceId);
        });

        modelBuilder.Entity<BudgetItemProjectionState>(entity =>
        {
            entity.ToTable("budget_item_projection_state");
            entity.HasKey(x => x.BudgetItemId);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => new { x.BudgetId, x.Name });
        });

        modelBuilder.Entity<BudgetAdjustmentProjectionState>(entity =>
        {
            entity.ToTable("budget_adjustment_projection_state");
            entity.HasKey(x => x.ActivityId);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(x => new { x.BudgetId, x.Date });
            entity.HasIndex(x => x.BudgetItemId);
            entity.HasIndex(x => x.SourceEventId);
        });

        modelBuilder.Entity<TransactionProjectionState>(entity =>
        {
            entity.ToTable("transaction_projection_state");
            entity.HasKey(x => x.TransactionId);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(x => new { x.BudgetId, x.TransactionDate });
        });

        modelBuilder.Entity<TransactionAllocationProjectionState>(entity =>
        {
            entity.ToTable("transaction_allocation_projection_state");
            entity.HasKey(x => new { x.TransactionId, x.BudgetItemId });
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BudgetId, x.BudgetItemId });
        });

        modelBuilder.Entity<ProjectionEventFailure>(entity =>
        {
            entity.ToTable("projection_event_failure");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Topic).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ConsumerGroup).HasMaxLength(160).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(160);
            entity.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.LastError).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.RawEventJson).IsRequired();
            entity.HasIndex(x => x.EventId);
            entity.HasIndex(x => new { x.Topic, x.Partition, x.Offset });
            entity.HasIndex(x => new { x.Status, x.LastFailedAt });
        });

        modelBuilder.Entity<AuditEventFailure>(entity =>
        {
            entity.ToTable("audit_event_failure");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Topic).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ConsumerGroup).HasMaxLength(160).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(160);
            entity.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.LastError).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.RawEventJson).IsRequired();
            entity.HasIndex(x => x.EventId);
            entity.HasIndex(x => new { x.Topic, x.Partition, x.Offset });
            entity.HasIndex(x => new { x.Status, x.LastFailedAt });
        });
    }
}
