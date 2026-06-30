using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class BudgetTests
{
    [Fact]
    public void BudgetCreatesBudgetItemWhenNameIsUnique()
    {
        var budget = Budget.Create("UK", "GBP");

        var result = budget.CreateBudgetItem(" Groceries ", BudgetItemKind.Consumption);

        var success = Assert.IsType<CreateBudgetItemResult.Success>(result);
        var modifiedBudget = success.Budget;
        var item = success.Item;
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
        var created = Assert.IsType<CreateBudgetItemResult.Success>(
            budget.CreateBudgetItem("Groceries", BudgetItemKind.Consumption));

        var result = created.Budget.CreateBudgetItem(" Groceries ", BudgetItemKind.Consumption);

        var duplicateName = Assert.IsType<CreateBudgetItemResult.DuplicateName>(result);
        Assert.Equal(Budget.DuplicateBudgetItemNameMessage, duplicateName.Error);
        Assert.Empty(budget.Items);
        Assert.Single(created.Budget.Items);
    }
}
