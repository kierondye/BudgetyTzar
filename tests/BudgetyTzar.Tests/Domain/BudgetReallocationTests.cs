using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class BudgetReallocationTests
{
    [Fact]
    public void ReallocationRequiresAtLeastTwoAdjustments()
    {
        var validationError = BudgetReallocation.ValidateAdjustments([
            new BudgetReallocationAdjustment(Guid.NewGuid(), 10m, BudgetAdjustmentType.Credit)
        ]);

        Assert.Equal("A reallocation must contain at least two adjustments.", validationError);
    }

    [Fact]
    public void ReallocationRequiresCreditsToEqualDebits()
    {
        var validationError = BudgetReallocation.ValidateAdjustments([
            new BudgetReallocationAdjustment(Guid.NewGuid(), 30m, BudgetAdjustmentType.Credit),
            new BudgetReallocationAdjustment(Guid.NewGuid(), 20m, BudgetAdjustmentType.Debit)
        ]);

        Assert.Equal("Reallocation credits must equal reallocation debits.", validationError);
    }

    [Fact]
    public void ReallocationCreatesLinkedBudgetAdjustments()
    {
        var budgetId = Guid.NewGuid();
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 5);
        var reallocation = BudgetReallocation.Create(budgetId, date, " Move budget ");

        var adjustments = reallocation.CreateLinkedAdjustments([
            new BudgetReallocationAdjustment(firstItemId, 30m, BudgetAdjustmentType.Credit),
            new BudgetReallocationAdjustment(secondItemId, 30m, BudgetAdjustmentType.Debit)
        ]);

        Assert.Equal(2, adjustments.Count);
        Assert.All(adjustments, x =>
        {
            Assert.Equal(budgetId, x.BudgetId);
            Assert.Equal(reallocation.Id, x.ReallocationId);
            Assert.Equal(date, x.Date);
            Assert.Equal("Move budget", x.Notes);
        });
        Assert.Contains(adjustments, x => x.BudgetItemId == firstItemId && x.Amount == 30m && x.Type == BudgetAdjustmentType.Credit);
        Assert.Contains(adjustments, x => x.BudgetItemId == secondItemId && x.Amount == 30m && x.Type == BudgetAdjustmentType.Debit);
    }
}
