using System.Text.Json;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Reporting;

public sealed class ReportingProjectionService(BudgetDbContext db)
{
    public async Task RebuildFromOutbox(CancellationToken ct)
    {
        db.PeriodBudgetSummaries.RemoveRange(db.PeriodBudgetSummaries);
        db.BudgetLinePeriodSummaries.RemoveRange(db.BudgetLinePeriodSummaries);
        db.CreditBudgetLinePeriodSummaries.RemoveRange(db.CreditBudgetLinePeriodSummaries);
        db.TransactionAssignmentSummaries.RemoveRange(db.TransactionAssignmentSummaries);
        db.CumulativeBudgetLineBalances.RemoveRange(db.CumulativeBudgetLineBalances);
        db.BudgetAuditTimelines.RemoveRange(db.BudgetAuditTimelines);
        await db.SaveChangesAsync(ct);

        var budgetIds = await db.OutboxMessages
            .Where(x => x.BudgetId.HasValue)
            .Select(x => x.BudgetId!.Value)
            .Distinct()
            .ToListAsync(ct);

        foreach (var budgetId in budgetIds)
        {
            await RebuildBudget(budgetId, ct);
        }
    }

    public async Task ProjectEnvelope(string envelopeJson, CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(envelopeJson, EventSerialization.Options);
        if (envelope?.Payload["budgetId"] is { } budgetIdNode)
        {
            var budgetId = budgetIdNode.GetValue<Guid>();
            await RebuildBudget(budgetId, ct);
        }
    }

    public async Task RebuildBudget(Guid budgetId, CancellationToken ct)
    {
        var periods = await db.BudgetPeriods
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .OrderBy(x => x.StartDate)
            .ToListAsync(ct);

        var existingPeriodIds = periods.Select(x => x.Id).ToArray();
        db.PeriodBudgetSummaries.RemoveRange(db.PeriodBudgetSummaries.Where(x => x.BudgetId == budgetId));
        db.BudgetLinePeriodSummaries.RemoveRange(db.BudgetLinePeriodSummaries.Where(x => x.BudgetId == budgetId));
        db.CreditBudgetLinePeriodSummaries.RemoveRange(db.CreditBudgetLinePeriodSummaries.Where(x => x.BudgetId == budgetId));
        db.TransactionAssignmentSummaries.RemoveRange(db.TransactionAssignmentSummaries.Where(x => x.BudgetId == budgetId));
        db.CumulativeBudgetLineBalances.RemoveRange(db.CumulativeBudgetLineBalances.Where(x => x.BudgetId == budgetId));
        db.BudgetAuditTimelines.RemoveRange(db.BudgetAuditTimelines.Where(x => x.BudgetId == budgetId));
        await db.SaveChangesAsync(ct);

        foreach (var period in periods)
        {
            var summary = await DashboardQueries.GetPeriodSummaryFromOperationalTables(db, budgetId, period.Id, ct);
            if (summary is null)
            {
                continue;
            }

            db.PeriodBudgetSummaries.Add(new PeriodBudgetSummaryProjection
            {
                BudgetId = budgetId,
                BudgetPeriodId = period.Id,
                PeriodName = summary.PeriodName,
                StartDate = summary.StartDate,
                EndDate = summary.EndDate,
                PlannedDebit = summary.PlannedDebit,
                ActualDebit = summary.ActualDebit,
                DebitRemaining = summary.DebitRemaining,
                DebitVariance = summary.DebitVariance,
                PlannedCredit = summary.PlannedCredit,
                ActualCredit = summary.ActualCredit,
                CreditVariance = summary.CreditVariance,
                UnassignedDebitTotal = summary.UnassignedDebitTotal,
                UnassignedCreditTotal = summary.UnassignedCreditTotal,
                PartiallyAssignedDebitTotal = summary.PartiallyAssignedDebitTotal,
                PartiallyAssignedCreditTotal = summary.PartiallyAssignedCreditTotal
            });

            db.BudgetLinePeriodSummaries.AddRange(summary.Lines.Select(line => new BudgetLinePeriodSummaryProjection
            {
                BudgetId = budgetId,
                BudgetPeriodId = period.Id,
                BudgetLineId = line.BudgetLineId,
                Name = line.Name,
                Direction = line.Direction,
                RolloverType = line.RolloverType,
                OpeningBalance = line.OpeningBalance,
                Allocated = line.Allocated,
                ReallocationIn = line.ReallocationIn,
                ReallocationOut = line.ReallocationOut,
                ActualAmount = line.ActualAmount,
                AdjustmentAmount = line.AdjustmentAmount,
                ClosingBalance = line.ClosingBalance,
                IsOverBudget = line.IsOverBudget,
                IsArchived = line.IsArchived
            }));

            db.CreditBudgetLinePeriodSummaries.AddRange(summary.Lines
                .Where(x => x.Direction == BudgetLineDirection.Credit)
                .Select(line => new CreditBudgetLinePeriodSummaryProjection
                {
                    BudgetId = budgetId,
                    BudgetPeriodId = period.Id,
                    BudgetLineId = line.BudgetLineId,
                    PeriodName = summary.PeriodName,
                    StartDate = summary.StartDate,
                    EndDate = summary.EndDate,
                    BudgetLineName = line.Name,
                    PlannedCredit = line.Allocated,
                    ActualCredit = line.ActualAmount,
                    CreditVariance = line.ActualAmount - line.Allocated
                }));

            db.CumulativeBudgetLineBalances.AddRange(summary.Lines
                .Where(x => x.RolloverType == BudgetLineRolloverType.Cumulative)
                .Select(line => new CumulativeBudgetLineBalanceProjection
                {
                    BudgetId = budgetId,
                    BudgetPeriodId = period.Id,
                    BudgetLineId = line.BudgetLineId,
                    OpeningBalance = line.OpeningBalance,
                    ClosingBalance = line.ClosingBalance
                }));
        }

        await ProjectTransactions(budgetId, ct);
        await ProjectAuditTimeline(budgetId, ct);
        var projectedAt = DateTimeOffset.UtcNow;
        await db.OutboxMessages
            .Where(x => x.BudgetId == budgetId && x.ProjectedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProjectedAt, projectedAt), ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task ProjectTransactions(Guid budgetId, CancellationToken ct)
    {
        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .ToListAsync(ct);
        var transactionIds = transactions.Select(x => x.Id).ToArray();
        var assignmentTotals = await db.TransactionAssignments
            .AsNoTracking()
            .Where(x => transactionIds.Contains(x.TransactionId))
            .GroupBy(x => x.TransactionId)
            .Select(x => new { TransactionId = x.Key, AssignedAmount = x.Sum(y => y.Amount) })
            .ToDictionaryAsync(x => x.TransactionId, x => x.AssignedAmount, ct);
        var periods = await db.BudgetPeriods
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .ToListAsync(ct);

        db.TransactionAssignmentSummaries.AddRange(transactions.Select(transaction =>
        {
            var assignedAmount = assignmentTotals.GetValueOrDefault(transaction.Id);
            var periodId = periods.FirstOrDefault(x =>
                x.StartDate <= transaction.TransactionDate
                && transaction.TransactionDate <= x.EndDate)?.Id;
            return new TransactionAssignmentSummaryProjection
            {
                TransactionId = transaction.Id,
                BudgetId = budgetId,
                BudgetPeriodId = periodId,
                TransactionAmount = transaction.Amount,
                AssignedAmount = assignedAmount,
                UnassignedAmount = Math.Max(0, transaction.Amount - assignedAmount),
                Direction = transaction.Direction,
                IsIgnored = transaction.IsIgnored
            };
        }));
    }

    private async Task ProjectAuditTimeline(Guid budgetId, CancellationToken ct)
    {
        var audits = await db.AuditEvents
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.BudgetPeriodId.HasValue)
            .ToListAsync(ct);

        db.BudgetAuditTimelines.AddRange(audits.Select(audit => new BudgetAuditTimelineProjection
        {
            AuditEventId = audit.Id,
            BudgetId = audit.BudgetId,
            BudgetPeriodId = audit.BudgetPeriodId,
            OccurredAt = audit.OccurredAt,
            EventType = audit.EventType,
            EntityType = audit.EntityType,
            EntityId = audit.EntityId,
            Description = audit.Description
        }));
    }
}
