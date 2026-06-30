using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class BudgetTests
{
    [Fact]
    public void BudgetCreatesBudgetItemWhenNameIsUnique()
    {
        var budget = Budget.Create("UK", "GBP");

        var item = budget.CreateBudgetItem(" Groceries ", BudgetItemKind.Consumption);

        Assert.Equal(budget.Id, item.BudgetId);
        Assert.Equal("Groceries", item.Name);
        Assert.Equal(BudgetItemKind.Consumption, item.Kind);
        Assert.Same(item, Assert.Single(budget.Items));
    }

    [Fact]
    public void BudgetRejectsDuplicateBudgetItemName()
    {
        var budget = Budget.Create("UK", "GBP");
        budget.CreateBudgetItem("Groceries", BudgetItemKind.Consumption);

        var validationError = budget.ValidateBudgetItemName(" Groceries ");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            budget.CreateBudgetItem(" Groceries ", BudgetItemKind.Consumption));

        Assert.Equal(Budget.DuplicateBudgetItemNameMessage, validationError);
        Assert.Equal(Budget.DuplicateBudgetItemNameMessage, exception.Message);
    }
}
