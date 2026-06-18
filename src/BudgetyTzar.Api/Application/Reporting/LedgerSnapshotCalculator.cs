using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Reporting;

public sealed record BudgetSnapshot(
    Guid BudgetId,
    DateOnly Date,
    decimal UnbudgetedBalance,
    decimal TotalBalance,
    IReadOnlyList<BudgetSnapshotItem> BudgetItems);

public sealed record BudgetSnapshotItem(
    Guid BudgetItemId,
    string Name,
    decimal Balance);

file sealed record BudgetSnapshotCalculationItem(
    Guid BudgetItemId,
    string Name,
    decimal Balance,
    decimal PlannedCredit,
    decimal PlannedDebit,
    decimal ActualCredit,
    decimal ActualDebit,
    bool IsArchived);

public static class LedgerSnapshotCalculator
{
    public static async Task<BudgetSnapshot?> Calculate(BudgetDbContext db, Guid budgetId, DateOnly date, CancellationToken ct)
    {
        if (!await db.Budgets.AnyAsync(x => x.Id == budgetId, ct))
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
                && x.Date <= date)
            .ToListAsync(ct);

        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.TransactionDate <= date && !x.IsIgnored)
            .ToListAsync(ct);
        var transactionIds = transactions.Select(x => x.Id).ToArray();
        var assignments = await db.TransactionAssignments
            .AsNoTracking()
            .Where(x => transactionIds.Contains(x.TransactionId))
            .ToListAsync(ct);
        var transactionsById = transactions.ToDictionary(x => x.Id);
        var assignmentTotals = assignments
            .GroupBy(x => x.TransactionId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));

        var calculatedItems = lines
            .Select(line =>
            {
                var lineAdjustments = adjustments.Where(x => x.BudgetLineId == line.Id).ToList();
                var lineAssignments = assignments.Where(x => x.BudgetLineId == line.Id).ToList();
                var plannedCredit = lineAdjustments
                    .Where(x => x.Type == BudgetAdjustmentType.Credit)
                    .Sum(x => x.Amount);
                var plannedDebit = lineAdjustments
                    .Where(x => x.Type == BudgetAdjustmentType.Debit)
                    .Sum(x => x.Amount);
                var actualCredit = lineAssignments
                    .Where(x => transactionsById[x.TransactionId].Direction == TransactionDirection.Credit)
                    .Sum(x => x.Amount);
                var actualDebit = lineAssignments
                    .Where(x => transactionsById[x.TransactionId].Direction == TransactionDirection.Debit)
                    .Sum(x => x.Amount);
                var balance = plannedDebit - plannedCredit + actualCredit - actualDebit;

                return new BudgetSnapshotCalculationItem(
                    line.Id,
                    line.Name,
                    balance,
                    plannedCredit,
                    plannedDebit,
                    actualCredit,
                    actualDebit,
                    line.IsArchived);
            })
            .Where(x => !x.IsArchived
                || x.Balance != 0
                || x.PlannedCredit != 0
                || x.PlannedDebit != 0
                || x.ActualCredit != 0
                || x.ActualDebit != 0)
            .ToList();

        var totalTransactionBalance = transactions.Sum(x => x.Direction == TransactionDirection.Credit ? x.Amount : -x.Amount);
        var totalBudgetedBalance = calculatedItems.Sum(x => x.Balance);
        var unbudgetedBalance = 0m;
        if (transactions.Count > 0)
        {
            var latestTransactionDate = transactions.Max(x => x.TransactionDate);
            var budgetedBalanceAtLatestTransaction = calculatedItems.Sum(item =>
            {
                var plannedCreditAtLatestTransaction = adjustments
                    .Where(x => x.BudgetLineId == item.BudgetItemId
                        && x.Date <= latestTransactionDate
                        && x.Type == BudgetAdjustmentType.Credit)
                    .Sum(x => x.Amount);
                var plannedDebitAtLatestTransaction = adjustments
                    .Where(x => x.BudgetLineId == item.BudgetItemId
                        && x.Date <= latestTransactionDate
                        && x.Type == BudgetAdjustmentType.Debit)
                    .Sum(x => x.Amount);

                return plannedDebitAtLatestTransaction - plannedCreditAtLatestTransaction + item.ActualCredit - item.ActualDebit;
            });
            unbudgetedBalance = totalTransactionBalance - budgetedBalanceAtLatestTransaction;
        }

        return new BudgetSnapshot(
            budgetId,
            date,
            unbudgetedBalance,
            totalBudgetedBalance + unbudgetedBalance,
            calculatedItems
                .Select(x => new BudgetSnapshotItem(x.BudgetItemId, x.Name, x.Balance))
                .ToList());
    }
}
