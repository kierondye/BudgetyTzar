using Microsoft.EntityFrameworkCore;
using BudgetyTzar.Api.Infrastructure.Persistence;

namespace BudgetyTzar.Api.Application.Reporting;

public sealed record BudgetLineSummary(
    Guid BudgetLineId,
    string Name,
    BudgetLineDirection Direction,
    BudgetLineRolloverType RolloverType,
    decimal OpeningBalance,
    decimal Allocated,
    decimal ReallocationIn,
    decimal ReallocationOut,
    decimal ActualAmount,
    decimal AdjustmentAmount,
    decimal ClosingBalance,
    bool IsOverBudget,
    bool IsArchived);

public sealed record PeriodSummary(
    Guid BudgetId,
    Guid BudgetPeriodId,
    string PeriodName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal PlannedDebit,
    decimal ActualDebit,
    decimal DebitRemaining,
    decimal DebitVariance,
    decimal PlannedCredit,
    decimal ActualCredit,
    decimal CreditVariance,
    decimal UnassignedDebitTotal,
    decimal UnassignedCreditTotal,
    decimal PartiallyAssignedDebitTotal,
    decimal PartiallyAssignedCreditTotal,
    IReadOnlyList<BudgetLineSummary> Lines);

public static class DashboardCalculator
{
    public static PeriodSummary Calculate(
        BudgetPeriod period,
        IReadOnlyCollection<BudgetPeriod> periods,
        IReadOnlyCollection<BudgetLine> budgetLines,
        IReadOnlyCollection<BudgetLineAllocation> allocations,
        IReadOnlyCollection<FinancialTransaction> transactions,
        IReadOnlyCollection<TransactionAssignment> assignments,
        IReadOnlyCollection<BudgetReallocation> reallocations,
        IReadOnlyCollection<BudgetAdjustment>? adjustments = null)
    {
        adjustments ??= [];
        var orderedPeriods = periods
            .Where(x => x.BudgetId == period.BudgetId && x.EndDate <= period.EndDate)
            .OrderBy(x => x.StartDate)
            .ThenBy(x => x.EndDate)
            .ToList();
        var assignmentTotals = assignments
            .GroupBy(x => x.TransactionId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));

        var closingBalances = new Dictionary<Guid, decimal>();
        var lineSnapshots = new List<BudgetLineSummary>();

        foreach (var currentPeriod in orderedPeriods)
        {
            var periodTransactions = transactions
                .Where(x => !x.IsIgnored && x.TransactionDate >= currentPeriod.StartDate && x.TransactionDate <= currentPeriod.EndDate)
                .ToList();
            var periodTransactionsById = periodTransactions.ToDictionary(x => x.Id);
            var periodTransactionIds = periodTransactions.Select(x => x.Id).ToHashSet();
            var periodAssignments = assignments
                .Where(x => periodTransactionIds.Contains(x.TransactionId))
                .ToList();
            var periodReallocations = reallocations
                .Where(x => x.BudgetPeriodId == currentPeriod.Id)
                .ToList();
            var periodAdjustments = adjustments
                .Where(x => x.BudgetPeriodId == currentPeriod.Id)
                .ToList();

            lineSnapshots = budgetLines
                .OrderBy(x => x.Direction)
                .ThenBy(x => x.Name)
                .Select(line =>
                {
                    var openingBalance = line.RolloverType == BudgetLineRolloverType.Cumulative
                        ? closingBalances.GetValueOrDefault(line.Id)
                        : 0m;
                    var allocated = allocations
                        .Where(x => x.BudgetPeriodId == currentPeriod.Id && x.BudgetLineId == line.Id)
                        .Sum(x => x.Amount);
                    var reallocationIn = periodReallocations
                        .Where(x => x.ToBudgetLineId == line.Id)
                        .Sum(x => x.Amount);
                    var reallocationOut = periodReallocations
                        .Where(x => x.FromBudgetLineId == line.Id)
                        .Sum(x => x.Amount);
                    var actualAmount = periodAssignments
                        .Where(x => x.BudgetLineId == line.Id)
                        .Sum(x => SignedAssignmentAmount(x, line, periodTransactionsById[x.TransactionId]));
                    var adjustmentAmount = periodAdjustments
                        .Where(x => x.BudgetLineId == line.Id)
                        .Sum(x => x.Amount);
                    var closingBalance = line.Direction == BudgetLineDirection.Debit
                        ? openingBalance + allocated + reallocationIn - reallocationOut - actualAmount + adjustmentAmount
                        : actualAmount - allocated + adjustmentAmount;

                    closingBalances[line.Id] = closingBalance;

                    return new BudgetLineSummary(
                        line.Id,
                        line.Name,
                        line.Direction,
                        line.RolloverType,
                        openingBalance,
                        allocated,
                        reallocationIn,
                        reallocationOut,
                        actualAmount,
                        adjustmentAmount,
                        closingBalance,
                        line.Direction == BudgetLineDirection.Debit && closingBalance < 0,
                        line.IsArchived);
                })
                .Where(LineShouldAppear)
                .ToList();
        }

        var selectedTransactions = transactions
            .Where(x => x.TransactionDate >= period.StartDate && x.TransactionDate <= period.EndDate && !x.IsIgnored)
            .ToList();
        var unassignedDebit = selectedTransactions
            .Where(x => x.Direction == TransactionDirection.Debit && assignmentTotals.GetValueOrDefault(x.Id) == 0)
            .Sum(x => x.Amount);
        var unassignedCredit = selectedTransactions
            .Where(x => x.Direction == TransactionDirection.Credit && assignmentTotals.GetValueOrDefault(x.Id) == 0)
            .Sum(x => x.Amount);
        var partiallyAssignedDebit = selectedTransactions
            .Where(x => x.Direction == TransactionDirection.Debit
                && assignmentTotals.GetValueOrDefault(x.Id) > 0
                && assignmentTotals.GetValueOrDefault(x.Id) < x.Amount)
            .Sum(x => x.Amount - assignmentTotals.GetValueOrDefault(x.Id));
        var partiallyAssignedCredit = selectedTransactions
            .Where(x => x.Direction == TransactionDirection.Credit
                && assignmentTotals.GetValueOrDefault(x.Id) > 0
                && assignmentTotals.GetValueOrDefault(x.Id) < x.Amount)
            .Sum(x => x.Amount - assignmentTotals.GetValueOrDefault(x.Id));

        var plannedDebit = lineSnapshots
            .Where(x => x.Direction == BudgetLineDirection.Debit)
            .Sum(x => x.Allocated);
        var actualDebit = lineSnapshots
            .Where(x => x.Direction == BudgetLineDirection.Debit)
            .Sum(x => x.ActualAmount);
        var debitRemaining = lineSnapshots
            .Where(x => x.Direction == BudgetLineDirection.Debit)
            .Sum(x => x.ClosingBalance);
        var plannedCredit = lineSnapshots
            .Where(x => x.Direction == BudgetLineDirection.Credit)
            .Sum(x => x.Allocated);
        var actualCredit = lineSnapshots
            .Where(x => x.Direction == BudgetLineDirection.Credit)
            .Sum(x => x.ActualAmount);

        return new PeriodSummary(
            period.BudgetId,
            period.Id,
            period.Name,
            period.StartDate,
            period.EndDate,
            plannedDebit,
            actualDebit,
            debitRemaining,
            plannedDebit - actualDebit,
            plannedCredit,
            actualCredit,
            actualCredit - plannedCredit,
            unassignedDebit,
            unassignedCredit,
            partiallyAssignedDebit,
            partiallyAssignedCredit,
            lineSnapshots);
    }

    private static bool LineShouldAppear(BudgetLineSummary line) =>
        !line.IsArchived
        || line.OpeningBalance != 0
        || line.Allocated != 0
        || line.ReallocationIn != 0
        || line.ReallocationOut != 0
        || line.ActualAmount != 0
        || line.AdjustmentAmount != 0
        || line.ClosingBalance != 0;

    private static decimal SignedAssignmentAmount(
        TransactionAssignment assignment,
        BudgetLine line,
        FinancialTransaction transaction)
    {
        var sameDirection = line.Direction switch
        {
            BudgetLineDirection.Debit => transaction.Direction == TransactionDirection.Debit,
            BudgetLineDirection.Credit => transaction.Direction == TransactionDirection.Credit,
            _ => false
        };

        return sameDirection ? assignment.Amount : -assignment.Amount;
    }
}

public static class DashboardQueries
{
    public static async Task<PeriodSummary?> GetPeriodSummary(
        BudgetDbContext db,
        Guid budgetId,
        Guid budgetPeriodId,
        CancellationToken cancellationToken)
    {
        var period = await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == budgetPeriodId && x.BudgetId == budgetId, cancellationToken);
        if (period is null)
        {
            return null;
        }

        var periods = await db.BudgetPeriods
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.EndDate <= period.EndDate)
            .ToListAsync(cancellationToken);
        var budgetLines = await db.BudgetLines
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .ToListAsync(cancellationToken);
        var periodIds = periods.Select(x => x.Id).ToArray();
        var allocations = await db.BudgetLineAllocations
            .AsNoTracking()
            .Where(x => periodIds.Contains(x.BudgetPeriodId))
            .ToListAsync(cancellationToken);
        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.TransactionDate <= period.EndDate)
            .ToListAsync(cancellationToken);
        var transactionIds = transactions.Select(x => x.Id).ToArray();
        var assignments = await db.TransactionAssignments
            .AsNoTracking()
            .Where(x => transactionIds.Contains(x.TransactionId))
            .ToListAsync(cancellationToken);
        var reallocations = await db.BudgetReallocations
            .AsNoTracking()
            .Where(x => periodIds.Contains(x.BudgetPeriodId))
            .ToListAsync(cancellationToken);
        var adjustments = await db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => periodIds.Contains(x.BudgetPeriodId))
            .ToListAsync(cancellationToken);

        return DashboardCalculator.Calculate(period, periods, budgetLines, allocations, transactions, assignments, reallocations, adjustments);
    }
}
