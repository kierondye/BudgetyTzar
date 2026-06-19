using Microsoft.EntityFrameworkCore;
using BudgetyTzar.Api.Application.Reporting;

namespace BudgetyTzar.Api.Infrastructure.Persistence;

public sealed class BudgetDbContext(DbContextOptions<BudgetDbContext> options) : DbContext(options)
{
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<FinancialTransaction> Transactions => Set<FinancialTransaction>();
    public DbSet<TransactionAssignment> TransactionAssignments => Set<TransactionAssignment>();
    public DbSet<BudgetReallocation> BudgetReallocations => Set<BudgetReallocation>();
    public DbSet<BudgetAdjustment> BudgetAdjustments => Set<BudgetAdjustment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<BudgetSnapshotProjection> BudgetSnapshotProjections => Set<BudgetSnapshotProjection>();
    public DbSet<BudgetSnapshotItemProjection> BudgetSnapshotItemProjections => Set<BudgetSnapshotItemProjection>();
    public DbSet<BudgetAuditTimelineProjection> BudgetAuditTimelines => Set<BudgetAuditTimelineProjection>();
    public DbSet<ProcessedProjectionEvent> ProcessedProjectionEvents => Set<ProcessedProjectionEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<BudgetLine>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
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
            entity.HasIndex(x => x.BudgetLineId);
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
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
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
            entity.HasIndex(x => new { x.BudgetId, x.Date }).IsUnique();
        });

        modelBuilder.Entity<BudgetSnapshotItemProjection>(entity =>
        {
            entity.ToTable("budget_snapshot_item");
            entity.HasKey(x => new { x.SnapshotId, x.BudgetItemId });
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Balance).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BudgetId, x.Date });
        });

        modelBuilder.Entity<BudgetAuditTimelineProjection>(entity =>
        {
            entity.ToTable("budget_audit_timeline");
            entity.HasKey(x => x.AuditEventId);
            entity.Property(x => x.EventType).HasMaxLength(160).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(4000);
            entity.HasIndex(x => new { x.BudgetId, x.OccurredAt });
        });

        modelBuilder.Entity<ProcessedProjectionEvent>(entity =>
        {
            entity.ToTable("processed_projection_event");
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventType).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => new { x.BudgetId, x.ProcessedAt });
        });
    }
}
