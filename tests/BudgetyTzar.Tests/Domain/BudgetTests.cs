using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class BudgetTests
{
    [Fact]
    public void BudgetCreatesBudgetItemWhenNameIsUnique()
    {
        var budget = Budget.Create("UK", "GBP");

        var item = budget.CreateBudgetItem([], " Groceries ", BudgetItemKind.Consumption);

        Assert.Equal(budget.Id, item.BudgetId);
        Assert.Equal("Groceries", item.Name);
        Assert.Equal(BudgetItemKind.Consumption, item.Kind);
    }

    [Fact]
    public void BudgetRejectsDuplicateBudgetItemName()
    {
        var budget = Budget.Create("UK", "GBP");
        var existing = BudgetItem.Create(budget.Id, "Groceries", BudgetItemKind.Consumption);

        var validationError = budget.ValidateBudgetItemName([existing], " Groceries ");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            budget.CreateBudgetItem([existing], " Groceries ", BudgetItemKind.Consumption));

        Assert.Equal(Budget.DuplicateBudgetItemNameMessage, validationError);
        Assert.Equal(Budget.DuplicateBudgetItemNameMessage, exception.Message);
    }

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

    [Fact]
    public void BudgetRejectsConsumptionAdjustmentThatWouldBecomeFunding()
    {
        var budget = Budget.Create("UK", "GBP");
        var groceries = BudgetItem.Create(budget.Id, "Groceries", BudgetItemKind.Consumption);
        var existingDebit = BudgetAdjustment.Create(
            budget.Id,
            groceries.Id,
            75m,
            BudgetAdjustmentType.Debit,
            new DateOnly(2026, 6, 1),
            "Expected groceries");
        var correction = BudgetAdjustment.Create(
            budget.Id,
            groceries.Id,
            100m,
            BudgetAdjustmentType.Credit,
            new DateOnly(2026, 6, 2),
            "Correction");

        var validationError = budget.ValidateBudgetItemKindForAdjustment(groceries, [existingDebit], correction);

        Assert.Equal(Budget.ConsumptionBudgetItemBecameFundingMessage, validationError);
    }

    [Fact]
    public void BudgetAllowsConsumptionCreditCorrectionWithinExistingConsumptionBudget()
    {
        var budget = Budget.Create("UK", "GBP");
        var groceries = BudgetItem.Create(budget.Id, "Groceries", BudgetItemKind.Consumption);
        var existingDebit = BudgetAdjustment.Create(
            budget.Id,
            groceries.Id,
            100m,
            BudgetAdjustmentType.Debit,
            new DateOnly(2026, 6, 1),
            "Expected groceries");
        var correction = BudgetAdjustment.Create(
            budget.Id,
            groceries.Id,
            25m,
            BudgetAdjustmentType.Credit,
            new DateOnly(2026, 6, 2),
            "Correction");

        var validationError = budget.ValidateBudgetItemKindForAdjustment(groceries, [existingDebit], correction);

        Assert.Null(validationError);
    }

    [Fact]
    public void BudgetRejectsFundingAdjustmentThatWouldBecomeConsumption()
    {
        var budget = Budget.Create("UK", "GBP");
        var salary = BudgetItem.Create(budget.Id, "Salary", BudgetItemKind.Funding);
        var existingCredit = BudgetAdjustment.Create(
            budget.Id,
            salary.Id,
            75m,
            BudgetAdjustmentType.Credit,
            new DateOnly(2026, 6, 1),
            "Expected salary");
        var reversal = BudgetAdjustment.Create(
            budget.Id,
            salary.Id,
            100m,
            BudgetAdjustmentType.Debit,
            new DateOnly(2026, 6, 2),
            "Reversal");

        var validationError = budget.ValidateBudgetItemKindForAdjustment(salary, [existingCredit], reversal);

        Assert.Equal(Budget.FundingBudgetItemBecameConsumptionMessage, validationError);
    }

    [Fact]
    public void BudgetAllowsFundingDebitCorrectionWithinExistingFundingBudget()
    {
        var budget = Budget.Create("UK", "GBP");
        var salary = BudgetItem.Create(budget.Id, "Salary", BudgetItemKind.Funding);
        var existingCredit = BudgetAdjustment.Create(
            budget.Id,
            salary.Id,
            100m,
            BudgetAdjustmentType.Credit,
            new DateOnly(2026, 6, 1),
            "Expected salary");
        var reversal = BudgetAdjustment.Create(
            budget.Id,
            salary.Id,
            25m,
            BudgetAdjustmentType.Debit,
            new DateOnly(2026, 6, 2),
            "Reversal");

        var validationError = budget.ValidateBudgetItemKindForAdjustment(salary, [existingCredit], reversal);

        Assert.Null(validationError);
    }
}
