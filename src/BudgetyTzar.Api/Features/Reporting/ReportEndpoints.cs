using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace BudgetyTzar.Api.Features;

public sealed record AuditTimelineItem(Guid AuditEventId, DateTimeOffset OccurredAt, string EventType, string EntityType, Guid EntityId, Guid? BudgetPeriodId, string Description);
public sealed record ReconciliationReport(
    Guid BudgetId,
    Guid BudgetPeriodId,
    decimal ImportedDebitTotal,
    decimal ImportedCreditTotal,
    decimal ManualDebitTotal,
    decimal ManualCreditTotal,
    decimal AssignedDebitTotal,
    decimal AssignedCreditTotal,
    decimal IgnoredDebitTotal,
    decimal IgnoredCreditTotal,
    decimal UnassignedDebitTotal,
    decimal UnassignedCreditTotal,
    decimal PartiallyAssignedDebitTotal,
    decimal PartiallyAssignedCreditTotal,
    decimal ReallocationTotal,
    decimal AdjustmentTotal,
    int DuplicateCandidateCount,
    decimal DebitDifference,
    decimal CreditDifference);
public sealed record BudgetLineTrendItem(Guid BudgetPeriodId, string PeriodName, DateOnly StartDate, DateOnly EndDate, decimal Allocated, decimal ActualAmount, decimal AdjustmentAmount, decimal ClosingBalance, bool IsOverBudget);
public sealed record CreditVarianceItem(Guid BudgetPeriodId, string PeriodName, DateOnly StartDate, DateOnly EndDate, Guid BudgetLineId, string BudgetLineName, decimal PlannedCredit, decimal ActualCredit, decimal CreditVariance);

public static partial class Endpoints
{
    private static void MapReportEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/reports/period-summary", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
            await DashboardQueries.GetPeriodSummary(db, budgetId, periodId, ct) is { } summary
                ? Results.Ok(summary)
                : Results.NotFound());

        budgets.MapGet("/{budgetId:guid}/reports/period-summary.csv", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
            await DashboardQueries.GetPeriodSummary(db, budgetId, periodId, ct) is { } summary
                ? Results.Text(ToPeriodSummaryCsv(summary), "text/csv", Encoding.UTF8)
                : Results.NotFound());

        budgets.MapGet("/{budgetId:guid}/reports/reconciliation", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var report = await GetReconciliationReport(db, budgetId, periodId, ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        budgets.MapGet("/{budgetId:guid}/reports/budget-line-trends", async (
            Guid budgetId,
            Guid budgetLineId,
            DateOnly from,
            DateOnly to,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (to < from)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(to)] = ["End date must be on or after start date."]
                });
            }

            if (!await db.BudgetLines.AnyAsync(x => x.Id == budgetLineId && x.BudgetId == budgetId, ct))
            {
                return Results.NotFound();
            }

            var periods = await db.BudgetPeriods
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId && x.StartDate <= to && x.EndDate >= from)
                .OrderBy(x => x.StartDate)
                .ToListAsync(ct);

            var trends = new List<BudgetLineTrendItem>();
            foreach (var period in periods)
            {
                var summary = await DashboardQueries.GetPeriodSummary(db, budgetId, period.Id, ct);
                var line = summary?.Lines.FirstOrDefault(x => x.BudgetLineId == budgetLineId);
                if (summary is not null && line is not null)
                {
                    trends.Add(new BudgetLineTrendItem(
                        period.Id,
                        period.Name,
                        period.StartDate,
                        period.EndDate,
                        line.Allocated,
                        line.ActualAmount,
                        line.AdjustmentAmount,
                        line.ClosingBalance,
                        line.IsOverBudget));
                }
            }

            return Results.Ok(trends);
        });

        budgets.MapGet("/{budgetId:guid}/reports/credit-variance", async (
            Guid budgetId,
            DateOnly from,
            DateOnly to,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (to < from)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(to)] = ["End date must be on or after start date."]
                });
            }

            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var periods = await db.BudgetPeriods
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId && x.StartDate <= to && x.EndDate >= from)
                .OrderBy(x => x.StartDate)
                .ToListAsync(ct);

            var items = new List<CreditVarianceItem>();
            foreach (var period in periods)
            {
                var summary = await DashboardQueries.GetPeriodSummary(db, budgetId, period.Id, ct);
                if (summary is not null)
                {
                    items.AddRange(summary.Lines
                        .Where(x => x.Direction == BudgetLineDirection.Credit)
                        .OrderBy(x => x.Name)
                        .Select(x => new CreditVarianceItem(
                            period.Id,
                            period.Name,
                            period.StartDate,
                            period.EndDate,
                            x.BudgetLineId,
                            x.Name,
                            x.Allocated,
                            x.ActualAmount,
                            x.ActualAmount - x.Allocated)));
                }
            }

            return Results.Ok(items);
        });

        budgets.MapGet("/{budgetId:guid}/reports/audit-timeline", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var period = await db.BudgetPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == periodId && x.BudgetId == budgetId, ct);
            if (period is null)
            {
                return Results.NotFound();
            }

            var items = await db.AuditEvents
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId && (x.BudgetPeriodId == periodId || x.AppliesToAllPeriods))
                .Select(x => new AuditTimelineItem(
                    x.Id,
                    x.OccurredAt,
                    x.EventType,
                    x.EntityType,
                    x.EntityId,
                    x.BudgetPeriodId,
                    x.Description))
                .ToListAsync(ct);

            return Results.Ok(items.OrderByDescending(x => x.OccurredAt).ToList());
        });
    }
    private static async Task<ReconciliationReport?> GetReconciliationReport(
        BudgetDbContext db,
        Guid budgetId,
        Guid periodId,
        CancellationToken ct)
    {
        var period = await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == periodId && x.BudgetId == budgetId, ct);
        if (period is null)
        {
            return null;
        }

        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.TransactionDate >= period.StartDate && x.TransactionDate <= period.EndDate)
            .ToListAsync(ct);
        var transactionIds = transactions.Select(x => x.Id).ToArray();
        var assignmentTotals = await db.TransactionAssignments
            .AsNoTracking()
            .Where(x => transactionIds.Contains(x.TransactionId))
            .GroupBy(x => x.TransactionId)
            .Select(x => new { TransactionId = x.Key, Amount = x.Sum(y => y.Amount) })
            .ToDictionaryAsync(x => x.TransactionId, x => x.Amount, ct);
        var reallocations = await db.BudgetReallocations
            .AsNoTracking()
            .Where(x => x.BudgetPeriodId == periodId)
            .ToListAsync(ct);
        var adjustments = await db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => x.BudgetPeriodId == periodId)
            .ToListAsync(ct);
        var importBatchIds = await db.TransactionImportBatches
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var duplicateCandidates = await db.TransactionImportRows
            .AsNoTracking()
            .CountAsync(x => importBatchIds.Contains(x.ImportBatchId) && x.IsDuplicateCandidate && x.TransactionDate >= period.StartDate && x.TransactionDate <= period.EndDate, ct);

        var activeTransactions = transactions.Where(x => !x.IsIgnored).ToList();
        var assignedDebit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Debit)
            .Sum(x => Math.Min(x.Amount, assignmentTotals.GetValueOrDefault(x.Id)));
        var assignedCredit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Credit)
            .Sum(x => Math.Min(x.Amount, assignmentTotals.GetValueOrDefault(x.Id)));
        var unassignedDebit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Debit && assignmentTotals.GetValueOrDefault(x.Id) == 0)
            .Sum(x => x.Amount);
        var unassignedCredit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Credit && assignmentTotals.GetValueOrDefault(x.Id) == 0)
            .Sum(x => x.Amount);
        var partiallyAssignedDebit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Debit
                && assignmentTotals.GetValueOrDefault(x.Id) > 0
                && assignmentTotals.GetValueOrDefault(x.Id) < x.Amount)
            .Sum(x => x.Amount - assignmentTotals.GetValueOrDefault(x.Id));
        var partiallyAssignedCredit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Credit
                && assignmentTotals.GetValueOrDefault(x.Id) > 0
                && assignmentTotals.GetValueOrDefault(x.Id) < x.Amount)
            .Sum(x => x.Amount - assignmentTotals.GetValueOrDefault(x.Id));
        var debitDifference = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Debit)
            .Sum(x => x.Amount) - assignedDebit;
        var creditDifference = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Credit)
            .Sum(x => x.Amount) - assignedCredit;

        return new ReconciliationReport(
            budgetId,
            periodId,
            transactions.Where(x => x.ImportBatchId is not null && x.Direction == TransactionDirection.Debit).Sum(x => x.Amount),
            transactions.Where(x => x.ImportBatchId is not null && x.Direction == TransactionDirection.Credit).Sum(x => x.Amount),
            transactions.Where(x => x.ImportBatchId is null && x.Direction == TransactionDirection.Debit).Sum(x => x.Amount),
            transactions.Where(x => x.ImportBatchId is null && x.Direction == TransactionDirection.Credit).Sum(x => x.Amount),
            assignedDebit,
            assignedCredit,
            transactions.Where(x => x.IsIgnored && x.Direction == TransactionDirection.Debit).Sum(x => x.Amount),
            transactions.Where(x => x.IsIgnored && x.Direction == TransactionDirection.Credit).Sum(x => x.Amount),
            unassignedDebit,
            unassignedCredit,
            partiallyAssignedDebit,
            partiallyAssignedCredit,
            reallocations.Sum(x => x.Amount),
            adjustments.Sum(x => Math.Abs(x.Amount)),
            duplicateCandidates,
            debitDifference,
            creditDifference);
    }

    private static string ToPeriodSummaryCsv(PeriodSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("budgetLineId,name,direction,rolloverType,isArchived,openingBalance,allocated,reallocationIn,reallocationOut,actualAmount,adjustmentAmount,closingBalance,isOverBudget");
        foreach (var line in summary.Lines)
        {
            builder
                .Append(line.BudgetLineId).Append(',')
                .Append(EscapeCsv(line.Name)).Append(',')
                .Append(line.Direction).Append(',')
                .Append(line.RolloverType).Append(',')
                .Append(line.IsArchived).Append(',')
                .Append(line.OpeningBalance).Append(',')
                .Append(line.Allocated).Append(',')
                .Append(line.ReallocationIn).Append(',')
                .Append(line.ReallocationOut).Append(',')
                .Append(line.ActualAmount).Append(',')
                .Append(line.AdjustmentAmount).Append(',')
                .Append(line.ClosingBalance).Append(',')
                .Append(line.IsOverBudget)
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

}
