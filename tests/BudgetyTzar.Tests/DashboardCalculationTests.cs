using BudgetyTzar.Api;
using BudgetyTzar.Api.Features;
using Xunit;

namespace BudgetyTzar.Tests;

public sealed class DashboardCalculationTests
{
    private const string Currency = "GBP";

    [Fact]
    public void BudgetMovementsAdjustAvailableBudgetButDoNotAlterActualSpending()
    {
        var period = Period();
        var groceries = Category("Groceries");
        var dining = Category("Dining out");
        var grocerySpend = Transaction(period, 30m, TransactionDirection.Debit);

        var withoutMovement = DashboardCalculator.Calculate(
            period,
            [groceries, dining],
            [Allocation(period, groceries, 100m), Allocation(period, dining, 75m)],
            [],
            [grocerySpend],
            [CategoryAssignment(grocerySpend, groceries, 30m)],
            []);

        var withMovement = DashboardCalculator.Calculate(
            period,
            [groceries, dining],
            [Allocation(period, groceries, 100m), Allocation(period, dining, 75m)],
            [],
            [grocerySpend],
            [CategoryAssignment(grocerySpend, groceries, 30m)],
            [Movement(period, dining, groceries, 25m)]);

        Assert.Equal(30m, withoutMovement.Category(groceries).NetSpending());
        Assert.Equal(30m, withMovement.Category(groceries).NetSpending());
        Assert.Equal(70m, withoutMovement.Category(groceries).Remaining);
        Assert.Equal(95m, withMovement.Category(groceries).Remaining);
        Assert.Equal(175m, withoutMovement.TotalPlannedBudget);
        Assert.Equal(175m, withMovement.TotalPlannedBudget);
    }

    [Fact]
    public void CreditTransactionAssignedToSpendingCategoryReducesNetSpending()
    {
        var period = Period();
        var groceries = Category("Groceries");
        var shop = Transaction(period, 80m, TransactionDirection.Debit);
        var refund = Transaction(period, 15m, TransactionDirection.Credit);

        var dashboard = DashboardCalculator.Calculate(
            period,
            [groceries],
            [Allocation(period, groceries, 100m)],
            [],
            [shop, refund],
            [CategoryAssignment(shop, groceries, 80m), CategoryAssignment(refund, groceries, 15m)],
            []);

        Assert.Equal(65m, dashboard.TotalActualSpending);
        Assert.Equal(65m, dashboard.Category(groceries).NetSpending());
        Assert.Equal(35m, dashboard.Category(groceries).Remaining);
    }

    [Fact]
    public void IncomeAssignedToIncomeSourcesIsSeparateFromSpending()
    {
        var period = Period();
        var groceries = Category("Groceries");
        var salary = Income("Salary");
        var paycheck = Transaction(period, 2_500m, TransactionDirection.Credit);
        var food = Transaction(period, 40m, TransactionDirection.Debit);

        var dashboard = DashboardCalculator.Calculate(
            period,
            [groceries],
            [Allocation(period, groceries, 150m)],
            [IncomeExpectation(period, salary, 2_500m)],
            [paycheck, food],
            [IncomeAssignment(paycheck, salary, 2_500m), CategoryAssignment(food, groceries, 40m)],
            []);

        Assert.Equal(2_500m, dashboard.ExpectedIncome);
        Assert.Equal(2_500m, dashboard.ActualIncome);
        Assert.Equal(40m, dashboard.TotalActualSpending);
        Assert.Equal(110m, dashboard.Category(groceries).Remaining);
    }

    [Fact]
    public void MonthlyDashboardTotalsAreCalculatedCorrectly()
    {
        var period = Period();
        var groceries = Category("Groceries");
        var transport = Category("Transport");
        var salary = Income("Salary");
        var groceryShop = Transaction(period, 120m, TransactionDirection.Debit);
        var groceryRefund = Transaction(period, 20m, TransactionDirection.Credit);
        var busPass = Transaction(period, 45m, TransactionDirection.Debit);
        var paycheck = Transaction(period, 3_000m, TransactionDirection.Credit);

        var dashboard = DashboardCalculator.Calculate(
            period,
            [groceries, transport],
            [Allocation(period, groceries, 500m), Allocation(period, transport, 200m)],
            [IncomeExpectation(period, salary, 3_000m)],
            [groceryShop, groceryRefund, busPass, paycheck],
            [
                CategoryAssignment(groceryShop, groceries, 120m),
                CategoryAssignment(groceryRefund, groceries, 20m),
                CategoryAssignment(busPass, transport, 45m),
                IncomeAssignment(paycheck, salary, 3_000m)
            ],
            [Movement(period, groceries, transport, 100m)]);

        Assert.Equal(3_000m, dashboard.ExpectedIncome);
        Assert.Equal(3_000m, dashboard.ActualIncome);
        Assert.Equal(700m, dashboard.TotalPlannedBudget);
        Assert.Equal(145m, dashboard.TotalActualSpending);
        Assert.Equal(555m, dashboard.RemainingBudget);
        Assert.Equal(400m, dashboard.Category(groceries).AdjustedBudget());
        Assert.Equal(100m, dashboard.Category(groceries).NetSpending());
        Assert.Equal(300m, dashboard.Category(transport).AdjustedBudget());
        Assert.Equal(45m, dashboard.Category(transport).NetSpending());
    }

    private static BudgetPeriod Period() => new()
    {
        Name = "June 2026",
        StartDate = new DateOnly(2026, 6, 1),
        EndDate = new DateOnly(2026, 6, 30)
    };

    private static BudgetCategory Category(string name) => new()
    {
        Name = name,
        Type = BudgetCategoryType.MonthlyReset
    };

    private static IncomeSource Income(string name) => new()
    {
        Name = name,
        IsRecurring = true
    };

    private static CategoryAllocation Allocation(BudgetPeriod period, BudgetCategory category, decimal amount) => new()
    {
        BudgetPeriodId = period.Id,
        BudgetCategoryId = category.Id,
        Amount = amount,
        Currency = Currency
    };

    private static IncomeExpectation IncomeExpectation(BudgetPeriod period, IncomeSource source, decimal amount) => new()
    {
        BudgetPeriodId = period.Id,
        IncomeSourceId = source.Id,
        Amount = amount,
        Currency = Currency
    };

    private static FinancialTransaction Transaction(BudgetPeriod period, decimal amount, TransactionDirection direction) => new()
    {
        BudgetPeriodId = period.Id,
        TransactionDate = period.StartDate,
        Description = $"{direction} transaction",
        Amount = amount,
        Currency = Currency,
        Direction = direction
    };

    private static TransactionAssignment CategoryAssignment(FinancialTransaction transaction, BudgetCategory category, decimal amount) => new()
    {
        TransactionId = transaction.Id,
        TargetType = TransactionAssignmentTargetType.BudgetCategory,
        TargetId = category.Id,
        Amount = amount,
        Currency = Currency
    };

    private static TransactionAssignment IncomeAssignment(FinancialTransaction transaction, IncomeSource source, decimal amount) => new()
    {
        TransactionId = transaction.Id,
        TargetType = TransactionAssignmentTargetType.IncomeSource,
        TargetId = source.Id,
        Amount = amount,
        Currency = Currency
    };

    private static BudgetMovement Movement(BudgetPeriod period, BudgetCategory from, BudgetCategory to, decimal amount) => new()
    {
        BudgetPeriodId = period.Id,
        FromCategoryId = from.Id,
        ToCategoryId = to.Id,
        Amount = amount,
        Currency = Currency,
        Reason = "Rebalance monthly budget"
    };
}

internal static class DashboardTestExtensions
{
    public static CategoryDashboardLine Category(this MonthlyDashboard dashboard, BudgetCategory category) =>
        dashboard.Categories.Single(snapshot => snapshot.CategoryId == category.Id);

    public static decimal AdjustedBudget(this CategoryDashboardLine line) =>
        line.Allocated + line.MovementIn - line.MovementOut;

    public static decimal NetSpending(this CategoryDashboardLine line) =>
        line.ActualSpending - line.Refunds;
}
