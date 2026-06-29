using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Reporting;

file sealed record BudgetSnapshotCalculationItem(
    Guid BudgetItemId,
    string Name,
    BudgetItemKind Kind,
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

        var lines = await db.BudgetItems
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var lineIds = lines.Select(x => x.Id).ToArray();

        var adjustments = await db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => lineIds.Contains(x.BudgetItemId)
                && (x.BudgetId == budgetId || x.BudgetId == Guid.Empty)
                && x.Date <= date)
            .ToListAsync(ct);

        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.TransactionDate <= date && !x.IsIgnored)
            .ToListAsync(ct);
        var transactionIds = transactions.Select(x => x.Id).ToArray();
        var allocations = await db.TransactionAllocations
            .AsNoTracking()
            .Where(x => transactionIds.Contains(x.TransactionId))
            .ToListAsync(ct);
        var transactionsById = transactions.ToDictionary(x => x.Id);
        var allocationTotals = allocations
            .GroupBy(x => x.TransactionId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));

        var calculatedItems = lines
            .Select(line =>
            {
                var lineAdjustments = adjustments.Where(x => x.BudgetItemId == line.Id).ToList();
                var lineAllocations = allocations.Where(x => x.BudgetItemId == line.Id).ToList();
                var plannedCredit = lineAdjustments
                    .Where(x => x.Type == BudgetAdjustmentType.Credit)
                    .Sum(x => x.Amount);
                var plannedDebit = lineAdjustments
                    .Where(x => x.Type == BudgetAdjustmentType.Debit)
                    .Sum(x => x.Amount);
                var actualCredit = lineAllocations
                    .Where(x => transactionsById[x.TransactionId].Direction == TransactionDirection.Credit)
                    .Sum(x => x.Amount);
                var actualDebit = lineAllocations
                    .Where(x => transactionsById[x.TransactionId].Direction == TransactionDirection.Debit)
                    .Sum(x => x.Amount);
                var balance = actualCredit - plannedCredit + plannedDebit - actualDebit;

                return new BudgetSnapshotCalculationItem(
                    line.Id,
                    line.Name,
                    line.Kind,
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
                    .Where(x => x.BudgetItemId == item.BudgetItemId
                        && x.Date <= latestTransactionDate
                        && x.Type == BudgetAdjustmentType.Credit)
                    .Sum(x => x.Amount);
                var plannedDebitAtLatestTransaction = adjustments
                    .Where(x => x.BudgetItemId == item.BudgetItemId
                        && x.Date <= latestTransactionDate
                        && x.Type == BudgetAdjustmentType.Debit)
                    .Sum(x => x.Amount);

                return item.ActualCredit - plannedCreditAtLatestTransaction + plannedDebitAtLatestTransaction - item.ActualDebit;
            });
            unbudgetedBalance = totalTransactionBalance - budgetedBalanceAtLatestTransaction;
        }

        return new BudgetSnapshot(
            budgetId,
            date,
            unbudgetedBalance,
            totalBudgetedBalance + unbudgetedBalance,
            totalTransactionBalance,
            totalBudgetedBalance,
            calculatedItems
                .Select(x => new BudgetSnapshotItem(
                    x.BudgetItemId,
                    x.Name,
                    x.Kind,
                    x.Balance,
                    x.PlannedCredit,
                    x.PlannedDebit,
                    x.ActualCredit,
                    x.ActualDebit))
                .ToList());
    }

    public static async Task<BudgetSnapshot?> GetProjectedOrCalculate(
        BudgetDbContext db,
        Guid budgetId,
        DateOnly date,
        bool useProjectionBackedReports,
        CancellationToken ct)
    {
        if (!useProjectionBackedReports)
        {
            return await Calculate(db, budgetId, date, ct);
        }

        var projection = await db.BudgetSnapshotProjections
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.Date <= date)
            .OrderByDescending(x => x.Date)
            .FirstOrDefaultAsync(ct);
        if (projection is null)
        {
            return null;
        }

        var items = await db.BudgetSnapshotItemProjections
            .AsNoTracking()
            .Where(x => x.SnapshotId == projection.Id)
            .OrderBy(x => x.Name)
            .Select(x => new BudgetSnapshotItem(
                x.BudgetItemId,
                x.Name,
                x.Kind,
                x.Balance,
                x.PlannedCredit,
                x.PlannedDebit,
                x.ActualCredit,
                x.ActualDebit))
            .ToListAsync(ct);

        return new BudgetSnapshot(
            budgetId,
            date,
            projection.UnbudgetedBalance,
            projection.TotalBalance,
            projection.TotalTransactionBalance,
            projection.TotalBudgetedBalance,
            items);
    }
}
