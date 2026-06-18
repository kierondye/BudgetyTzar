using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
public sealed record BudgetLineTrendItem(Guid BudgetPeriodId, string PeriodName, DateOnly StartDate, DateOnly EndDate, decimal Allocated, decimal ActualAmount, decimal AdjustmentAmount, decimal ClosingBalance, bool IsOverBudget);
public sealed record CreditVarianceItem(Guid BudgetPeriodId, string PeriodName, DateOnly StartDate, DateOnly EndDate, Guid BudgetLineId, string BudgetLineName, decimal PlannedCredit, decimal ActualCredit, decimal CreditVariance);

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

        budgets.MapGet("/{budgetId:guid}/reports/reconciliation/date-range", async (
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
        }).ExcludeFromDescription();

        budgets.MapGet("/{budgetId:guid}/reports/audit-timeline/date-range", async (
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

            var fromDateTime = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var toDateTime = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var items = await db.AuditEvents
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId && x.OccurredAt >= fromDateTime && x.OccurredAt <= toDateTime)
                .OrderByDescending(x => x.OccurredAt)
                .Select(x => new AuditTimelineItem(
                    x.Id,
                    x.OccurredAt,
                    x.EventType,
                    x.EntityType,
                    x.EntityId,
                    x.BudgetPeriodId,
                    x.Description))
                .ToListAsync(ct);
            return Results.Ok(items);
        }).ExcludeFromDescription();

        budgets.MapGet("/{budgetId:guid}/reports/period-summary", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            IOptions<ProjectionOptions> projectionOptions,
            CancellationToken ct) =>
            await DashboardQueries.GetPeriodSummary(db, budgetId, periodId, projectionOptions.Value.UseProjectionBackedReports, ct) is { } summary
                ? Results.Ok(summary)
                : Results.NotFound()).ExcludeFromDescription();

        budgets.MapGet("/{budgetId:guid}/reports/period-summary.csv", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            IOptions<ProjectionOptions> projectionOptions,
            CancellationToken ct) =>
            await DashboardQueries.GetPeriodSummary(db, budgetId, periodId, projectionOptions.Value.UseProjectionBackedReports, ct) is { } summary
                ? Results.Text(ToPeriodSummaryCsv(summary), "text/csv", Encoding.UTF8)
                : Results.NotFound()).ExcludeFromDescription();

        budgets.MapGet("/{budgetId:guid}/reports/reconciliation", async (
            Guid budgetId,
            HttpContext httpContext,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            ReconciliationReport? report;
            if (TryReadPeriodId(httpContext, out var periodId))
            {
                report = await GetReconciliationReport(db, budgetId, periodId, ct);
            }
            else if (TryReadDateRange(httpContext, out var from, out var to, out var problem))
            {
                report = await GetReconciliationReport(db, budgetId, from, to, ct);
            }
            else
            {
                return Results.ValidationProblem(problem);
            }

            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        budgets.MapGet("/{budgetId:guid}/reports/budget-line-trends", async (
            Guid budgetId,
            Guid budgetLineId,
            DateOnly from,
            DateOnly to,
            BudgetDbContext db,
            IOptions<ProjectionOptions> projectionOptions,
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

            if (projectionOptions.Value.UseProjectionBackedReports
                && await db.BudgetLinePeriodSummaries.AnyAsync(x => x.BudgetId == budgetId && x.BudgetLineId == budgetLineId, ct))
            {
                var projectedTrends = await db.BudgetLinePeriodSummaries
                    .AsNoTracking()
                    .Where(x => x.BudgetId == budgetId
                        && x.BudgetLineId == budgetLineId
                        && db.PeriodBudgetSummaries.Any(p =>
                            p.BudgetPeriodId == x.BudgetPeriodId
                            && p.StartDate <= to
                            && p.EndDate >= from))
                    .Join(
                        db.PeriodBudgetSummaries.AsNoTracking(),
                        line => line.BudgetPeriodId,
                        period => period.BudgetPeriodId,
                        (line, period) => new BudgetLineTrendItem(
                            line.BudgetPeriodId,
                            period.PeriodName,
                            period.StartDate,
                            period.EndDate,
                            line.Allocated,
                            line.ActualAmount,
                            line.AdjustmentAmount,
                            line.ClosingBalance,
                            line.IsOverBudget))
                    .OrderBy(x => x.StartDate)
                    .ToListAsync(ct);

                return Results.Ok(projectedTrends);
            }

            var periods = await db.BudgetPeriods
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId && x.StartDate <= to && x.EndDate >= from)
                .OrderBy(x => x.StartDate)
                .ToListAsync(ct);

            var trends = new List<BudgetLineTrendItem>();
            foreach (var period in periods)
            {
                var summary = await DashboardQueries.GetPeriodSummary(db, budgetId, period.Id, false, ct);
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
        }).ExcludeFromDescription();

        budgets.MapGet("/{budgetId:guid}/reports/credit-variance", async (
            Guid budgetId,
            DateOnly from,
            DateOnly to,
            BudgetDbContext db,
            IOptions<ProjectionOptions> projectionOptions,
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

            if (projectionOptions.Value.UseProjectionBackedReports
                && await db.CreditBudgetLinePeriodSummaries.AnyAsync(x => x.BudgetId == budgetId, ct))
            {
                var projectedItems = await db.CreditBudgetLinePeriodSummaries
                    .AsNoTracking()
                    .Where(x => x.BudgetId == budgetId && x.StartDate <= to && x.EndDate >= from)
                    .OrderBy(x => x.StartDate)
                    .ThenBy(x => x.BudgetLineName)
                    .Select(x => new CreditVarianceItem(
                        x.BudgetPeriodId,
                        x.PeriodName,
                        x.StartDate,
                        x.EndDate,
                        x.BudgetLineId,
                        x.BudgetLineName,
                        x.PlannedCredit,
                        x.ActualCredit,
                        x.CreditVariance))
                    .ToListAsync(ct);

                return Results.Ok(projectedItems);
            }

            var periods = await db.BudgetPeriods
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId && x.StartDate <= to && x.EndDate >= from)
                .OrderBy(x => x.StartDate)
                .ToListAsync(ct);

            var items = new List<CreditVarianceItem>();
            foreach (var period in periods)
            {
                var summary = await DashboardQueries.GetPeriodSummary(db, budgetId, period.Id, false, ct);
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
        }).ExcludeFromDescription();

        budgets.MapGet("/{budgetId:guid}/reports/audit-timeline", async (
            Guid budgetId,
            HttpContext httpContext,
            BudgetDbContext db,
            IOptions<ProjectionOptions> projectionOptions,
            CancellationToken ct) =>
        {
            if (!TryReadPeriodId(httpContext, out var periodId))
            {
                if (!TryReadDateRange(httpContext, out var from, out var to, out var problem))
                {
                    return Results.ValidationProblem(problem);
                }

                if (!await BudgetExists(db, budgetId, ct))
                {
                    return Results.NotFound();
                }

                var fromDateTime = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var toDateTime = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
                var rangeItems = await db.AuditEvents
                    .AsNoTracking()
                    .Where(x => x.BudgetId == budgetId && x.OccurredAt >= fromDateTime && x.OccurredAt <= toDateTime)
                    .Select(x => new AuditTimelineItem(
                        x.Id,
                        x.OccurredAt,
                        x.EventType,
                        x.EntityType,
                        x.EntityId,
                        x.BudgetPeriodId,
                        x.Description))
                    .ToListAsync(ct);

                return Results.Ok(rangeItems.OrderByDescending(x => x.OccurredAt).ToList());
            }

            var period = await db.BudgetPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == periodId && x.BudgetId == budgetId, ct);
            if (period is null)
            {
                return Results.NotFound();
            }

            if (projectionOptions.Value.UseProjectionBackedReports
                && await db.BudgetAuditTimelines.AnyAsync(x => x.BudgetId == budgetId && x.BudgetPeriodId == periodId, ct))
            {
                var projectedItems = await db.BudgetAuditTimelines
                    .AsNoTracking()
                    .Where(x => x.BudgetId == budgetId && x.BudgetPeriodId == periodId)
                    .Select(x => new AuditTimelineItem(
                        x.AuditEventId,
                        x.OccurredAt,
                        x.EventType,
                        x.EntityType,
                        x.EntityId,
                        x.BudgetPeriodId,
                        x.Description))
                    .ToListAsync(ct);

                return Results.Ok(projectedItems.OrderByDescending(x => x.OccurredAt).ToList());
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

    private static bool TryReadPeriodId(HttpContext httpContext, out Guid periodId)
    {
        periodId = default;
        return httpContext.Request.Query.TryGetValue("periodId", out var value)
            && Guid.TryParse(value, out periodId);
    }

    private static bool TryReadDateRange(
        HttpContext httpContext,
        out DateOnly from,
        out DateOnly to,
        out Dictionary<string, string[]> problem)
    {
        from = default;
        to = default;
        problem = [];

        if (!httpContext.Request.Query.TryGetValue("from", out var fromValue)
            || !DateOnly.TryParse(fromValue, out from))
        {
            problem[nameof(from)] = ["A valid from date is required."];
        }

        if (!httpContext.Request.Query.TryGetValue("to", out var toValue)
            || !DateOnly.TryParse(toValue, out to))
        {
            problem[nameof(to)] = ["A valid to date is required."];
        }

        if (problem.Count == 0 && to < from)
        {
            problem[nameof(to)] = ["End date must be on or after start date."];
        }

        return problem.Count == 0;
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
