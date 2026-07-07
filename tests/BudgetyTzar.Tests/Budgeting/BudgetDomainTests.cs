using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Tests.Budgeting;

public sealed class BudgetDomainTests
{
    [Fact]
    public void Create_normalizes_name_and_starts_without_budget_items()
    {
        var budget = Budget.Create(Guid.NewGuid(), " UK ", Currency("GBP"));

        Assert.Equal("UK", budget.Name);
        Assert.Empty(budget.BudgetItems);
    }

    [Fact]
    public void Budget_item_updates_return_new_budget_without_mutating_existing_budget()
    {
        var budget = Budget.Create(Guid.NewGuid(), "UK", Currency("GBP"));
        var budgetItemId = Guid.NewGuid();
        var plannedAmount = Money("3000.00");

        var withBudgetItem = budget.AddBudgetItem(
            budgetItemId,
            " Salary ",
            BudgetItemKind.Funding,
            plannedAmount);
        var renamed = withBudgetItem.RenameBudgetItem(budgetItemId, "Pay");
        var updatedAmount = renamed.ChangeBudgetItemPlannedAmount(budgetItemId, Money("3200.00"));

        Assert.Empty(budget.BudgetItems);
        Assert.Equal("Salary", Assert.Single(withBudgetItem.BudgetItems).Name);
        Assert.Equal("Pay", Assert.Single(renamed.BudgetItems).Name);

        var updatedBudgetItem = Assert.Single(updatedAmount.BudgetItems);
        Assert.Equal(budgetItemId, updatedBudgetItem.BudgetItemId);
        Assert.Equal("Pay", updatedBudgetItem.Name);
        Assert.Equal(BudgetItemKind.Funding, updatedBudgetItem.Kind);
        Assert.Equal("3200.00", updatedBudgetItem.PlannedAmount.FormattedValue);
    }

    [Fact]
    public void Add_budget_item_rejects_duplicate_normalized_names()
    {
        var budget = Budget.Create(Guid.NewGuid(), "UK", Currency("GBP"))
            .AddBudgetItem(Guid.NewGuid(), "Salary", BudgetItemKind.Funding, Money("3000.00"));

        Assert.Throws<InvalidOperationException>(() =>
            budget.AddBudgetItem(Guid.NewGuid(), " Salary ", BudgetItemKind.Consumption, Money("100.00")));
    }

    [Fact]
    public void Rename_budget_item_rejects_duplicate_normalized_names()
    {
        var groceriesId = Guid.NewGuid();
        var budget = Budget.Create(Guid.NewGuid(), "UK", Currency("GBP"))
            .AddBudgetItem(Guid.NewGuid(), "Salary", BudgetItemKind.Funding, Money("3000.00"))
            .AddBudgetItem(groceriesId, "Groceries", BudgetItemKind.Consumption, Money("400.00"));

        Assert.Throws<InvalidOperationException>(() =>
            budget.RenameBudgetItem(groceriesId, " Salary "));
    }

    private static CurrencyCode Currency(string value)
    {
        return CurrencyCode.TryCreate(value, out var currency)
            ? currency
            : throw new InvalidOperationException("Invalid test currency.");
    }

    private static PositiveMoneyAmount Money(string value)
    {
        return PositiveMoneyAmount.TryCreate(value, out var amount)
            ? amount!
            : throw new InvalidOperationException("Invalid test amount.");
    }
}
