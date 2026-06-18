using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace BudgetyTzar.Api.Features;

public sealed record AuditTimelineItem(Guid AuditEventId, DateTimeOffset OccurredAt, string EventType, string EntityType, Guid EntityId, Guid? BudgetPeriodId, string Description);
public sealed record BudgetActivityReport(Guid BudgetId, DateOnly From, DateOnly To, IReadOnlyList<BudgetActivityItem> BudgetItems);
public sealed record BudgetActivityItem(Guid BudgetItemId, string Name, decimal PlannedCredit, decimal PlannedDebit, decimal ActualCredit, decimal ActualDebit, decimal NetActivity);
public sealed record BudgetItemTrendItem(Guid BudgetItemId, string Name, DateOnly From, DateOnly To, decimal PlannedCredit, decimal PlannedDebit, decimal ActualCredit, decimal ActualDebit, decimal NetActivity);
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

public static partial class Endpoints
{
    private static void MapReportEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/snapshot", async (
            Guid budgetId,
            DateOnly date,
            BudgetDbContext db,
            CancellationToken ct) =>
            await LedgerSnapshotCalculator.Calculate(db, budgetId, date, ct) is { } snapshot
                ? Results.Ok(snapshot)
                : Results.NotFound());

        budgets.MapGet("/{budgetId:guid}/reports/activity", async (
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

            var report = await GetActivityReport(db, budgetId, from, to, ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        budgets.MapGet("/{budgetId:guid}/reports/activity.csv", async (
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

            var report = await GetActivityReport(db, budgetId, from, to, ct);
            return report is null
                ? Results.NotFound()
                : Results.Text(ToActivityCsv(report), "text/csv", Encoding.UTF8);
        });

        budgets.MapGet("/{budgetId:guid}/reports/budget-item-trends", async (
            Guid budgetId,
            Guid budgetItemId,
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

            var report = await GetActivityReport(db, budgetId, from, to, ct);
            if (report is null)
            {
                return Results.NotFound();
            }

            return report.BudgetItems.FirstOrDefault(x => x.BudgetItemId == budgetItemId) is { } item
                ? Results.Ok(new[]
                {
                    new BudgetItemTrendItem(
                        item.BudgetItemId,
                        item.Name,
                        from,
                        to,
                        item.PlannedCredit,
                        item.PlannedDebit,
                        item.ActualCredit,
                        item.ActualDebit,
                        item.NetActivity)
                })
                : Results.NotFound();
        });

        budgets.MapGet("/{budgetId:guid}/reports/reconciliation", async (
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

            var report = await GetReconciliationReport(db, budgetId, from, to, ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        budgets.MapGet("/{budgetId:guid}/reports/audit-timeline", async (
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

            var fromDateTime = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var toDateTime = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
            var events = await db.AuditEvents
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId)
                .Select(x => new AuditTimelineItem(
                    x.Id,
                    x.OccurredAt,
                    x.EventType,
                    x.EntityType,
                    x.EntityId,
                    x.BudgetPeriodId,
                    x.Description))
                .ToListAsync(ct);

            var items = events
                .Where(x => x.OccurredAt >= fromDateTime && x.OccurredAt <= toDateTime)
                .OrderByDescending(x => x.OccurredAt)
                .ToList();

            return Results.Ok(items);
        });
    }

    private static async Task<BudgetActivityReport?> GetActivityReport(
        BudgetDbContext db,
        Guid budgetId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (!await BudgetExists(db, budgetId, ct))
        {
            return null;
        }

        var lines = await db.BudgetLines
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var lineIds = lines.Select(x => x.Id).ToArray();
        var adjustments = await db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => lineIds.Contains(x.BudgetLineId)
                && (x.BudgetId == budgetId || x.BudgetId == Guid.Empty)
                && x.Date >= from
                && x.Date <= to)
            .ToListAsync(ct);
        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && !x.IsIgnored && x.TransactionDate >= from && x.TransactionDate <= to)
            .ToListAsync(ct);
        var transactionIds = transactions.Select(x => x.Id).ToArray();
        var assignments = await db.TransactionAssignments
            .AsNoTracking()
            .Where(x => transactionIds.Contains(x.TransactionId))
            .ToListAsync(ct);
        var transactionsById = transactions.ToDictionary(x => x.Id);

        var items = lines
            .Select(line =>
            {
                var lineAdjustments = adjustments.Where(x => x.BudgetLineId == line.Id).ToList();
                var lineAssignments = assignments.Where(x => x.BudgetLineId == line.Id).ToList();
                var plannedCredit = lineAdjustments.Where(x => x.Type == BudgetAdjustmentType.Credit).Sum(x => x.Amount);
                var plannedDebit = lineAdjustments.Where(x => x.Type == BudgetAdjustmentType.Debit).Sum(x => x.Amount);
                var actualCredit = lineAssignments
                    .Where(x => transactionsById[x.TransactionId].Direction == TransactionDirection.Credit)
                    .Sum(x => x.Amount);
                var actualDebit = lineAssignments
                    .Where(x => transactionsById[x.TransactionId].Direction == TransactionDirection.Debit)
                    .Sum(x => x.Amount);
                return new BudgetActivityItem(
                    line.Id,
                    line.Name,
                    plannedCredit,
                    plannedDebit,
                    actualCredit,
                    actualDebit,
                    plannedCredit - plannedDebit + actualCredit - actualDebit);
            })
            .Where(x => x.PlannedCredit != 0 || x.PlannedDebit != 0 || x.ActualCredit != 0 || x.ActualDebit != 0)
            .ToList();

        return new BudgetActivityReport(budgetId, from, to, items);
    }

    private static async Task<ReconciliationReport?> GetReconciliationReport(
        BudgetDbContext db,
        Guid budgetId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (!await BudgetExists(db, budgetId, ct))
        {
            return null;
        }

        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.TransactionDate >= from && x.TransactionDate <= to)
            .ToListAsync(ct);
        var transactionIds = transactions.Select(x => x.Id).ToArray();
        var assignmentTotals = await db.TransactionAssignments
            .AsNoTracking()
            .Where(x => transactionIds.Contains(x.TransactionId))
            .GroupBy(x => x.TransactionId)
            .Select(x => new { TransactionId = x.Key, Amount = x.Sum(y => y.Amount) })
            .ToDictionaryAsync(x => x.TransactionId, x => x.Amount, ct);
        var adjustments = await db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => (x.BudgetId == budgetId || x.BudgetId == Guid.Empty) && x.Date >= from && x.Date <= to)
            .ToListAsync(ct);
        var reallocations = await db.BudgetReallocations
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.Date >= from && x.Date <= to)
            .ToListAsync(ct);
        var importBatchIds = await db.TransactionImportBatches
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var duplicateCandidates = await db.TransactionImportRows
            .AsNoTracking()
            .CountAsync(x => importBatchIds.Contains(x.ImportBatchId) && x.IsDuplicateCandidate && x.TransactionDate >= from && x.TransactionDate <= to, ct);
        var activeTransactions = transactions.Where(x => !x.IsIgnored).ToList();
        var assignedDebit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Debit)
            .Sum(x => Math.Min(x.Amount, assignmentTotals.GetValueOrDefault(x.Id)));
        var assignedCredit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Credit)
            .Sum(x => Math.Min(x.Amount, assignmentTotals.GetValueOrDefault(x.Id)));

        return new ReconciliationReport(
            budgetId,
            Guid.Empty,
            transactions.Where(x => x.ImportBatchId is not null && x.Direction == TransactionDirection.Debit).Sum(x => x.Amount),
            transactions.Where(x => x.ImportBatchId is not null && x.Direction == TransactionDirection.Credit).Sum(x => x.Amount),
            transactions.Where(x => x.ImportBatchId is null && x.Direction == TransactionDirection.Debit).Sum(x => x.Amount),
            transactions.Where(x => x.ImportBatchId is null && x.Direction == TransactionDirection.Credit).Sum(x => x.Amount),
            assignedDebit,
            assignedCredit,
            transactions.Where(x => x.IsIgnored && x.Direction == TransactionDirection.Debit).Sum(x => x.Amount),
            transactions.Where(x => x.IsIgnored && x.Direction == TransactionDirection.Credit).Sum(x => x.Amount),
            activeTransactions.Where(x => x.Direction == TransactionDirection.Debit && assignmentTotals.GetValueOrDefault(x.Id) == 0).Sum(x => x.Amount),
            activeTransactions.Where(x => x.Direction == TransactionDirection.Credit && assignmentTotals.GetValueOrDefault(x.Id) == 0).Sum(x => x.Amount),
            activeTransactions.Where(x => x.Direction == TransactionDirection.Debit && assignmentTotals.GetValueOrDefault(x.Id) > 0 && assignmentTotals.GetValueOrDefault(x.Id) < x.Amount).Sum(x => x.Amount - assignmentTotals.GetValueOrDefault(x.Id)),
            activeTransactions.Where(x => x.Direction == TransactionDirection.Credit && assignmentTotals.GetValueOrDefault(x.Id) > 0 && assignmentTotals.GetValueOrDefault(x.Id) < x.Amount).Sum(x => x.Amount - assignmentTotals.GetValueOrDefault(x.Id)),
            reallocations.Count,
            adjustments.Sum(x => x.Amount),
            duplicateCandidates,
            activeTransactions.Where(x => x.Direction == TransactionDirection.Debit).Sum(x => x.Amount) - assignedDebit,
            activeTransactions.Where(x => x.Direction == TransactionDirection.Credit).Sum(x => x.Amount) - assignedCredit);
    }

    private static string EscapeCsv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static string ToActivityCsv(BudgetActivityReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("budgetItemId,name,plannedCredit,plannedDebit,actualCredit,actualDebit,netActivity");
        foreach (var item in report.BudgetItems)
        {
            builder
                .Append(item.BudgetItemId).Append(',')
                .Append(EscapeCsv(item.Name)).Append(',')
                .Append(item.PlannedCredit).Append(',')
                .Append(item.PlannedDebit).Append(',')
                .Append(item.ActualCredit).Append(',')
                .Append(item.ActualDebit).Append(',')
                .Append(item.NetActivity)
                .AppendLine();
        }

        return builder.ToString();
    }

}
