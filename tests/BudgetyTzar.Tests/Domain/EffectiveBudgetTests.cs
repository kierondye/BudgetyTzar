using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class EffectiveBudgetTests
{
    [Fact]
    public void EffectiveBudgetAllowsDebitAdjustmentWhenPlannedFundingCoversItByDate()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 7, 2);
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            100m,
            [new EffectiveBudgetItemState(groceries, 0m)]);

        var itemLookup = effectiveBudget.GetBudgetItem(groceries.Id);
        var item = Assert.IsType<EffectiveBudgetItemFound>(itemLookup).Item;
        var result = item.CreateAdjustment(100m, BudgetAdjustmentType.Debit, "Covered spending");

        var recorded = Assert.IsType<EffectiveBudgetAdjustmentRecorded>(result);
        var adjustment = recorded.Adjustment;
        Assert.Equal("Groceries", recorded.BudgetItemName);
        Assert.Equal(budgetId, adjustment.BudgetId);
        Assert.Equal(groceries.Id, adjustment.BudgetItemId);
        Assert.Equal(date, adjustment.Date);
        Assert.Equal(100m, adjustment.Amount);
        Assert.Equal(BudgetAdjustmentType.Debit, adjustment.Type);
        Assert.Equal("Covered spending", adjustment.Notes);
    }

    [Fact]
    public void EffectiveBudgetRejectsDebitAdjustmentWhenPlannedFundingDoesNotCoverItByDate()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 1);
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            0m,
            [new EffectiveBudgetItemState(groceries, 0m)]);

        var itemLookup = effectiveBudget.GetBudgetItem(groceries.Id);
        var item = Assert.IsType<EffectiveBudgetItemFound>(itemLookup).Item;
        var result = item.CreateAdjustment(100m, BudgetAdjustmentType.Debit, "Too early");

        var validationProblem = Assert.IsType<EffectiveBudgetAdjustmentValidationProblem>(result);
        Assert.Equal(EffectiveBudget.NetPlannedSpendingExceededMessage, validationProblem.Error);
    }

    [Fact]
    public void EffectiveBudgetRejectsConsumptionAdjustmentThatWouldBecomeFunding()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 2);
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            25m,
            [new EffectiveBudgetItemState(groceries, -75m)]);

        var itemLookup = effectiveBudget.GetBudgetItem(groceries.Id);
        var item = Assert.IsType<EffectiveBudgetItemFound>(itemLookup).Item;
        var result = item.CreateAdjustment(100m, BudgetAdjustmentType.Credit, "Correction");

        var validationProblem = Assert.IsType<EffectiveBudgetAdjustmentValidationProblem>(result);
        Assert.Equal(BudgetItem.ConsumptionBudgetItemBecameFundingMessage, validationProblem.Error);
    }

    [Fact]
    public void EffectiveBudgetAllowsConsumptionCreditCorrectionWithinExistingConsumptionBudget()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 2);
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            0m,
            [new EffectiveBudgetItemState(groceries, -100m)]);

        var itemLookup = effectiveBudget.GetBudgetItem(groceries.Id);
        var item = Assert.IsType<EffectiveBudgetItemFound>(itemLookup).Item;
        var result = item.CreateAdjustment(25m, BudgetAdjustmentType.Credit, "Correction");

        var recorded = Assert.IsType<EffectiveBudgetAdjustmentRecorded>(result);
        var adjustment = recorded.Adjustment;
        Assert.Equal(date, adjustment.Date);
        Assert.Equal(groceries.Id, adjustment.BudgetItemId);
    }

    [Fact]
    public void EffectiveBudgetRejectsFundingAdjustmentThatWouldBecomeConsumption()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 2);
        var salary = BudgetItem.Create(budgetId, "Salary", BudgetItemKind.Funding);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            200m,
            [new EffectiveBudgetItemState(salary, 75m)]);

        var itemLookup = effectiveBudget.GetBudgetItem(salary.Id);
        var item = Assert.IsType<EffectiveBudgetItemFound>(itemLookup).Item;
        var result = item.CreateAdjustment(100m, BudgetAdjustmentType.Debit, "Reversal");

        var validationProblem = Assert.IsType<EffectiveBudgetAdjustmentValidationProblem>(result);
        Assert.Equal(BudgetItem.FundingBudgetItemBecameConsumptionMessage, validationProblem.Error);
    }

    [Fact]
    public void EffectiveBudgetAllowsFundingDebitCorrectionWithinExistingFundingBudget()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 2);
        var salary = BudgetItem.Create(budgetId, "Salary", BudgetItemKind.Funding);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            100m,
            [new EffectiveBudgetItemState(salary, 100m)]);

        var itemLookup = effectiveBudget.GetBudgetItem(salary.Id);
        var item = Assert.IsType<EffectiveBudgetItemFound>(itemLookup).Item;
        var result = item.CreateAdjustment(25m, BudgetAdjustmentType.Debit, "Reversal");

        var recorded = Assert.IsType<EffectiveBudgetAdjustmentRecorded>(result);
        var adjustment = recorded.Adjustment;
        Assert.Equal(date, adjustment.Date);
        Assert.Equal(salary.Id, adjustment.BudgetItemId);
    }

    [Fact]
    public void EffectiveBudgetReturnsNotFoundWhenLookingUpUnknownItem()
    {
        var effectiveBudget = new EffectiveBudget(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 2),
            0m,
            []);

        var result = effectiveBudget.GetBudgetItem(Guid.NewGuid());

        Assert.IsType<EffectiveBudgetItemNotFound>(result);
    }

    [Fact]
    public void EffectiveBudgetRejectsAdjustmentForArchivedItemAfterArchiveDate()
    {
        var budgetId = Guid.NewGuid();
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        groceries.Archive(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            new DateOnly(2026, 6, 2),
            100m,
            [new EffectiveBudgetItemState(groceries, 0m)]);

        var itemLookup = effectiveBudget.GetBudgetItem(groceries.Id);
        var item = Assert.IsType<EffectiveBudgetItemFound>(itemLookup).Item;
        var result = item.CreateAdjustment(25m, BudgetAdjustmentType.Debit, "After archive");

        Assert.IsType<EffectiveBudgetAdjustmentArchivedBudgetItem>(result);
    }

    [Fact]
    public void EffectiveBudgetRejectsItemsFromAnotherBudget()
    {
        var budgetId = Guid.NewGuid();
        var otherBudgetItem = BudgetItem.Create(Guid.NewGuid(), "Groceries", BudgetItemKind.Consumption);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EffectiveBudget(
                budgetId,
                new DateOnly(2026, 6, 2),
                0m,
                [new EffectiveBudgetItemState(otherBudgetItem, 0m)]));

        Assert.Equal("Effective budget items must belong to the effective budget.", exception.Message);
    }
}
