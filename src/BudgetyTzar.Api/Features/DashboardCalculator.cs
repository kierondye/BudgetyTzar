using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CategoryDashboardLine(
    Guid CategoryId,
    string CategoryName,
    BudgetCategoryType CategoryType,
    decimal Allocated,
    decimal MovementIn,
    decimal MovementOut,
    decimal ActualSpending,
    decimal Refunds,
    decimal Remaining,
    bool IsOverBudget);

public sealed record MonthlyDashboard(
    Guid BudgetPeriodId,
    string PeriodName,
    decimal ExpectedIncome,
    decimal ActualIncome,
    decimal IncomeVariance,
    decimal TotalPlannedBudget,
    decimal TotalActualSpending,
    decimal RemainingBudget,
    IReadOnlyList<CategoryDashboardLine> Categories);

public static class DashboardCalculator
{
    public static MonthlyDashboard Calculate(
        BudgetPeriod period,
        IReadOnlyCollection<BudgetCategory> categories,
        IReadOnlyCollection<CategoryAllocation> allocations,
        IReadOnlyCollection<IncomeExpectation> incomeExpectations,
        IReadOnlyCollection<FinancialTransaction> transactions,
        IReadOnlyCollection<TransactionAssignment> assignments,
        IReadOnlyCollection<BudgetMovement> movements)
    {
        var categoryLines = categories
            .Where(category => !category.IsArchived)
            .OrderBy(category => category.Name)
            .Select(category =>
            {
                var allocated = allocations
                    .Where(x => x.BudgetCategoryId == category.Id)
                    .Sum(x => x.Amount);
                var movementIn = movements
                    .Where(x => x.ToCategoryId == category.Id)
                    .Sum(x => x.Amount);
                var movementOut = movements
                    .Where(x => x.FromCategoryId == category.Id)
                    .Sum(x => x.Amount);

                var categoryAssignments = assignments
                    .Where(x => x.TargetType == TransactionAssignmentTargetType.BudgetCategory && x.TargetId == category.Id)
                    .Join(
                        transactions.Where(x => !x.IsIgnored),
                        assignment => assignment.TransactionId,
                        transaction => transaction.Id,
                        (assignment, transaction) => new { assignment.Amount, transaction.Direction });

                var spending = categoryAssignments
                    .Where(x => x.Direction == TransactionDirection.Debit)
                    .Sum(x => x.Amount);
                var refunds = categoryAssignments
                    .Where(x => x.Direction == TransactionDirection.Credit)
                    .Sum(x => x.Amount);
                var netSpending = spending - refunds;
                var remaining = allocated + movementIn - movementOut - netSpending;

                return new CategoryDashboardLine(
                    category.Id,
                    category.Name,
                    category.Type,
                    allocated,
                    movementIn,
                    movementOut,
                    spending,
                    refunds,
                    remaining,
                    remaining < 0);
            })
            .ToList();

        var actualIncome = assignments
            .Where(x => x.TargetType == TransactionAssignmentTargetType.IncomeSource)
            .Join(
                transactions.Where(x => !x.IsIgnored && x.Direction == TransactionDirection.Credit),
                assignment => assignment.TransactionId,
                transaction => transaction.Id,
                (assignment, _) => assignment.Amount)
            .Sum();

        var expectedIncome = incomeExpectations.Sum(x => x.Amount);
        var plannedBudget = categoryLines.Sum(x => x.Allocated);
        var actualSpending = categoryLines.Sum(x => x.ActualSpending - x.Refunds);

        return new MonthlyDashboard(
            period.Id,
            period.Name,
            expectedIncome,
            actualIncome,
            actualIncome - expectedIncome,
            plannedBudget,
            actualSpending,
            categoryLines.Sum(x => x.Remaining),
            categoryLines);
    }
}

public static class DashboardQueries
{
    public static async Task<MonthlyDashboard?> GetMonthlyDashboard(
        Data.BudgetDbContext db,
        Guid budgetPeriodId,
        CancellationToken cancellationToken)
    {
        var period = await db.BudgetPeriods.FirstOrDefaultAsync(x => x.Id == budgetPeriodId, cancellationToken);
        if (period is null)
        {
            return null;
        }

        var categories = await db.BudgetCategories.AsNoTracking().ToListAsync(cancellationToken);
        var allocations = await db.CategoryAllocations
            .AsNoTracking()
            .Where(x => x.BudgetPeriodId == budgetPeriodId)
            .ToListAsync(cancellationToken);
        var incomeExpectations = await db.IncomeExpectations
            .AsNoTracking()
            .Where(x => x.BudgetPeriodId == budgetPeriodId)
            .ToListAsync(cancellationToken);
        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(x => x.BudgetPeriodId == budgetPeriodId)
            .ToListAsync(cancellationToken);
        var transactionIds = transactions.Select(x => x.Id).ToArray();
        var assignments = await db.TransactionAssignments
            .AsNoTracking()
            .Where(x => transactionIds.Contains(x.TransactionId))
            .ToListAsync(cancellationToken);
        var movements = await db.BudgetMovements
            .AsNoTracking()
            .Where(x => x.BudgetPeriodId == budgetPeriodId)
            .ToListAsync(cancellationToken);

        return DashboardCalculator.Calculate(period, categories, allocations, incomeExpectations, transactions, assignments, movements);
    }
}
