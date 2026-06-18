using Microsoft.EntityFrameworkCore;
using BudgetyTzar.Api.Application.Reporting;

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
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<PeriodBudgetSummaryProjection> PeriodBudgetSummaries => Set<PeriodBudgetSummaryProjection>();
    public DbSet<BudgetLinePeriodSummaryProjection> BudgetLinePeriodSummaries => Set<BudgetLinePeriodSummaryProjection>();
    public DbSet<CreditBudgetLinePeriodSummaryProjection> CreditBudgetLinePeriodSummaries => Set<CreditBudgetLinePeriodSummaryProjection>();
    public DbSet<TransactionAssignmentSummaryProjection> TransactionAssignmentSummaries => Set<TransactionAssignmentSummaryProjection>();
    public DbSet<CumulativeBudgetLineBalanceProjection> CumulativeBudgetLineBalances => Set<CumulativeBudgetLineBalanceProjection>();
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
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => x.BudgetPeriodId);
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
            entity.HasIndex(x => x.BudgetPeriodId);
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

        modelBuilder.Entity<PeriodBudgetSummaryProjection>(entity =>
        {
            entity.ToTable("period_budget_summary");
            entity.HasKey(x => x.BudgetPeriodId);
            entity.Property(x => x.PeriodName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PlannedDebit).HasPrecision(18, 2);
            entity.Property(x => x.ActualDebit).HasPrecision(18, 2);
            entity.Property(x => x.DebitRemaining).HasPrecision(18, 2);
            entity.Property(x => x.DebitVariance).HasPrecision(18, 2);
            entity.Property(x => x.PlannedCredit).HasPrecision(18, 2);
            entity.Property(x => x.ActualCredit).HasPrecision(18, 2);
            entity.Property(x => x.CreditVariance).HasPrecision(18, 2);
            entity.Property(x => x.UnassignedDebitTotal).HasPrecision(18, 2);
            entity.Property(x => x.UnassignedCreditTotal).HasPrecision(18, 2);
            entity.Property(x => x.PartiallyAssignedDebitTotal).HasPrecision(18, 2);
            entity.Property(x => x.PartiallyAssignedCreditTotal).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BudgetId, x.StartDate });
        });

        modelBuilder.Entity<BudgetLinePeriodSummaryProjection>(entity =>
        {
            entity.ToTable("budget_line_period_summary");
            entity.HasKey(x => new { x.BudgetPeriodId, x.BudgetLineId });
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.RolloverType).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.OpeningBalance).HasPrecision(18, 2);
            entity.Property(x => x.Allocated).HasPrecision(18, 2);
            entity.Property(x => x.ReallocationIn).HasPrecision(18, 2);
            entity.Property(x => x.ReallocationOut).HasPrecision(18, 2);
            entity.Property(x => x.ActualAmount).HasPrecision(18, 2);
            entity.Property(x => x.AdjustmentAmount).HasPrecision(18, 2);
            entity.Property(x => x.ClosingBalance).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BudgetId, x.BudgetLineId });
        });

        modelBuilder.Entity<CreditBudgetLinePeriodSummaryProjection>(entity =>
        {
            entity.ToTable("credit_budget_line_period_summary");
            entity.HasKey(x => new { x.BudgetPeriodId, x.BudgetLineId });
            entity.Property(x => x.PeriodName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.BudgetLineName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PlannedCredit).HasPrecision(18, 2);
            entity.Property(x => x.ActualCredit).HasPrecision(18, 2);
            entity.Property(x => x.CreditVariance).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BudgetId, x.StartDate });
        });

        modelBuilder.Entity<TransactionAssignmentSummaryProjection>(entity =>
        {
            entity.ToTable("transaction_assignment_summary");
            entity.HasKey(x => x.TransactionId);
            entity.Property(x => x.TransactionAmount).HasPrecision(18, 2);
            entity.Property(x => x.AssignedAmount).HasPrecision(18, 2);
            entity.Property(x => x.UnassignedAmount).HasPrecision(18, 2);
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(x => new { x.BudgetId, x.BudgetPeriodId });
        });

        modelBuilder.Entity<CumulativeBudgetLineBalanceProjection>(entity =>
        {
            entity.ToTable("cumulative_budget_line_balance");
            entity.HasKey(x => new { x.BudgetPeriodId, x.BudgetLineId });
            entity.Property(x => x.OpeningBalance).HasPrecision(18, 2);
            entity.Property(x => x.ClosingBalance).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BudgetId, x.BudgetLineId });
        });

        modelBuilder.Entity<BudgetAuditTimelineProjection>(entity =>
        {
            entity.ToTable("budget_audit_timeline");
            entity.HasKey(x => x.AuditEventId);
            entity.Property(x => x.EventType).HasMaxLength(160).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.BudgetId, x.BudgetPeriodId, x.OccurredAt });
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
