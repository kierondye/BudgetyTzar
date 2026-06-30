using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class BudgetTests
{
    [Fact]
    public void BudgetCreatesBudgetItemWhenNameIsUnique()
    {
        var budget = Budget.Create("UK", "GBP");

        var (modifiedBudget, item) = budget.CreateBudgetItem(" Groceries ", BudgetItemKind.Consumption);

        Assert.Equal(budget.Id, item.BudgetId);
        Assert.Equal("Groceries", item.Name);
        Assert.Equal(BudgetItemKind.Consumption, item.Kind);
        Assert.Empty(budget.Items);
        Assert.NotSame(budget, modifiedBudget);
        Assert.Same(item, Assert.Single(modifiedBudget.Items));
        Assert.Equal(budget.Id, modifiedBudget.Id);
        Assert.Equal(budget.Name, modifiedBudget.Name);
        Assert.Equal(budget.Currency, modifiedBudget.Currency);
        Assert.Equal(budget.CreatedAt, modifiedBudget.CreatedAt);
    }

    [Fact]
    public void BudgetRejectsDuplicateBudgetItemName()
    {
        var budget = Budget.Create("UK", "GBP");
        var (modifiedBudget, _) = budget.CreateBudgetItem("Groceries", BudgetItemKind.Consumption);

        var validationError = modifiedBudget.ValidateBudgetItemName(" Groceries ");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            modifiedBudget.CreateBudgetItem(" Groceries ", BudgetItemKind.Consumption));

        Assert.Equal(Budget.DuplicateBudgetItemNameMessage, validationError);
        Assert.Equal(Budget.DuplicateBudgetItemNameMessage, exception.Message);
        Assert.Empty(budget.Items);
        Assert.Single(modifiedBudget.Items);
    }
}
