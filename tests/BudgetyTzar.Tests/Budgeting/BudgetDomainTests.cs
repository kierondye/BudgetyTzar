using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Tests.Budgeting;

public sealed class BudgetDomainTests
{
    [Fact]
    public void Create_normalizes_name_and_starts_without_budget_items()
    {
        var budget = CreateBudget(" UK ");

        Assert.Equal("UK", budget.Name.Value);
        Assert.Empty(budget.BudgetItems);
    }

    [Fact]
    public void Budget_item_updates_return_new_budget_without_mutating_existing_budget()
    {
        var budget = CreateBudget("UK");
        var budgetItemId = Guid.NewGuid();
        var plannedAmount = Money("3000.00");

        var added = Assert.IsType<AddBudgetItemResult.Added>(budget.AddBudgetItem(
            budgetItemId,
            Name(" Salary "),
            BudgetItemKind.Funding,
            plannedAmount));
        var withBudgetItem = added.Budget;
        var renamed = Assert.IsType<RenameBudgetItemResult.Renamed>(
            withBudgetItem.RenameBudgetItem(budgetItemId, Name("Pay")));
        var updatedAmount = Assert.IsType<ChangeBudgetItemPlannedAmountResult.Changed>(
            renamed.Budget.ChangeBudgetItemPlannedAmount(budgetItemId, Money("3200.00")));

        Assert.Empty(budget.BudgetItems);
        Assert.Equal("Salary", Assert.Single(withBudgetItem.BudgetItems).Name.Value);
        Assert.Equal("Pay", Assert.Single(renamed.Budget.BudgetItems).Name.Value);

        var updatedBudgetItem = Assert.Single(updatedAmount.Budget.BudgetItems);
        Assert.Equal(budgetItemId, updatedBudgetItem.BudgetItemId);
        Assert.Equal("Pay", updatedBudgetItem.Name.Value);
        Assert.Equal(BudgetItemKind.Funding, updatedBudgetItem.Kind);
        Assert.Equal("3200.00", updatedBudgetItem.PlannedAmount.FormattedValue);
    }

    [Fact]
    public void Mutations_append_immutable_audit_facts_with_complete_budget_values()
    {
        var budget = CreateBudget("UK");
        var budgetItemId = Guid.NewGuid();

        var added = Assert.IsType<AddBudgetItemResult.Added>(
            budget.AddBudgetItem(budgetItemId, Name("Salary"), BudgetItemKind.Funding, Money("3000.00")));

        Assert.Single(budget.AuditFacts);
        Assert.Equal(2, added.Budget.AuditFacts.Length);

        var fact = added.Budget.AuditFacts[^1];
        Assert.NotEqual(Guid.Empty, fact.Id);
        Assert.Equal(AuditAction.BudgetItemCreated, fact.Action);
        Assert.Contains(budget.BudgetId.ToString(), fact.OldValue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(budgetItemId.ToString(), fact.NewValue, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(Budget.AuditFacts), fact.OldValue, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(Budget.AuditFacts), fact.NewValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_budget_item_rejects_duplicate_normalized_names()
    {
        var budget = CreateBudget("UK");
        var added = Assert.IsType<AddBudgetItemResult.Added>(
            budget.AddBudgetItem(Guid.NewGuid(), Name("Salary"), BudgetItemKind.Funding, Money("3000.00")));

        var result = added.Budget.AddBudgetItem(Guid.NewGuid(), Name(" Salary "), BudgetItemKind.Consumption, Money("100.00"));

        Assert.IsType<AddBudgetItemResult.DuplicateName>(result);
    }

    [Fact]
    public void Rename_budget_item_rejects_duplicate_normalized_names()
    {
        var groceriesId = Guid.NewGuid();
        var budget = CreateBudget("UK");
        var withSalary = Assert.IsType<AddBudgetItemResult.Added>(
            budget.AddBudgetItem(Guid.NewGuid(), Name("Salary"), BudgetItemKind.Funding, Money("3000.00")));
        var withGroceries = Assert.IsType<AddBudgetItemResult.Added>(
            withSalary.Budget.AddBudgetItem(groceriesId, Name("Groceries"), BudgetItemKind.Consumption, Money("400.00")));

        var result = withGroceries.Budget.RenameBudgetItem(groceriesId, Name(" Salary "));

        Assert.IsType<RenameBudgetItemResult.DuplicateName>(result);
    }

    private static Budget CreateBudget(string name)
    {
        return Assert.IsType<CreateBudgetResult.Created>(
            Budget.Create(Guid.NewGuid(), Name(name), Currency("GBP"))).Budget;
    }

    private static NormalizedName Name(string value)
    {
        return NormalizedName.TryCreate(value, out var name)
            ? name
            : throw new InvalidOperationException("Invalid test name.");
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
