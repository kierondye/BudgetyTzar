using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class BudgetAdjustmentTests
{
    [Fact]
    public void SignedPlannedAmountTreatsCreditsAsPositiveAndDebitsAsNegative()
    {
        var budgetId = Guid.NewGuid();
        var budgetItemId = Guid.NewGuid();

        var credit = BudgetAdjustment.Create(
            budgetId,
            budgetItemId,
            100m,
            BudgetAdjustmentType.Credit,
            new DateOnly(2026, 6, 1),
            null);
        var debit = BudgetAdjustment.Create(
            budgetId,
            budgetItemId,
            25m,
            BudgetAdjustmentType.Debit,
            new DateOnly(2026, 6, 1),
            null);

        Assert.Equal(100m, credit.SignedPlannedAmount());
        Assert.Equal(-25m, debit.SignedPlannedAmount());
    }
}
