using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class BudgetTests
{
    [Fact]
    public void BudgetAllowsDebitAdjustmentWhenPlannedIncomeCoversItByDate()
    {
        var budget = Budget.Create("UK", "GBP");
        var salary = BudgetAdjustment.Create(
            budget.Id,
            Guid.NewGuid(),
            100m,
            BudgetAdjustmentType.Credit,
            new DateOnly(2026, 7, 1),
            "Future income");
        var groceries = BudgetAdjustment.Create(
            budget.Id,
            Guid.NewGuid(),
            100m,
            BudgetAdjustmentType.Debit,
            new DateOnly(2026, 7, 2),
            "Covered spending");

        var canRecord = budget.CanRecordAdjustment([salary], groceries);

        Assert.True(canRecord);
    }

    [Fact]
    public void BudgetRejectsDebitAdjustmentWhenPlannedIncomeIsAfterAdjustmentDate()
    {
        var budget = Budget.Create("UK", "GBP");
        var salary = BudgetAdjustment.Create(
            budget.Id,
            Guid.NewGuid(),
            100m,
            BudgetAdjustmentType.Credit,
            new DateOnly(2026, 7, 1),
            "Future income");
        var groceries = BudgetAdjustment.Create(
            budget.Id,
            Guid.NewGuid(),
            100m,
            BudgetAdjustmentType.Debit,
            new DateOnly(2026, 6, 1),
            "Too early");

        var canRecord = budget.CanRecordAdjustment([salary], groceries);

        Assert.False(canRecord);
    }
}
