using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Tests.Budgeting;

public sealed class BudgetRepositoryTests
{
    [Fact]
    public void Add_rejects_duplicate_budget_names_without_overwriting_existing_budget()
    {
        var repository = new InMemoryBudgetRepository();
        var firstBudget = Budget.Create(Guid.NewGuid(), "UK", Currency("GBP"));
        var duplicateBudget = Budget.Create(Guid.NewGuid(), "UK", Currency("EUR"));

        var firstResult = repository.Add(firstBudget);
        var duplicateResult = repository.Add(duplicateBudget);

        Assert.IsType<AddBudgetResult.Added>(firstResult);
        Assert.IsType<AddBudgetResult.DuplicateName>(duplicateResult);

        var budget = Assert.Single(repository.GetAll());
        Assert.Equal(firstBudget.BudgetId, budget.BudgetId);
        Assert.Equal("GBP", budget.Currency.Value);
    }

    [Fact]
    public void TryUpdate_applies_each_update_to_the_latest_stored_budget()
    {
        var repository = new InMemoryBudgetRepository();
        var budget = Budget.Create(Guid.NewGuid(), "UK", Currency("GBP"));
        repository.Add(budget);

        var salaryId = Guid.NewGuid();
        var groceriesId = Guid.NewGuid();

        var firstResult = repository.TryUpdate(
            budget.BudgetId,
            currentBudget => currentBudget.AddBudgetItem(
                salaryId,
                "Salary",
                BudgetItemKind.Funding,
                Money("3000.00")));
        var secondResult = repository.TryUpdate(
            budget.BudgetId,
            currentBudget => currentBudget.AddBudgetItem(
                groceriesId,
                "Groceries",
                BudgetItemKind.Consumption,
                Money("400.00")));

        Assert.IsType<BudgetUpdateResult.Updated>(firstResult);
        Assert.IsType<BudgetUpdateResult.Updated>(secondResult);

        var updatedBudget = repository.Get(budget.BudgetId);
        Assert.NotNull(updatedBudget);
        Assert.Collection(
            updatedBudget.BudgetItems,
            budgetItem => Assert.Equal(salaryId, budgetItem.BudgetItemId),
            budgetItem => Assert.Equal(groceriesId, budgetItem.BudgetItemId));
    }

    [Fact]
    public void TryUpdate_can_report_conflict_without_changing_stored_budget()
    {
        var repository = new InMemoryBudgetRepository();
        var ukBudget = Budget.Create(Guid.NewGuid(), "UK", Currency("GBP"));
        var euBudget = Budget.Create(Guid.NewGuid(), "EU", Currency("EUR"));
        repository.Add(ukBudget);
        repository.Add(euBudget);

        var result = repository.TryUpdate(
            ukBudget.BudgetId,
            currentBudget => currentBudget.Rename("EU"));

        Assert.IsType<BudgetUpdateResult.Conflict>(result);
        Assert.Equal("UK", repository.Get(ukBudget.BudgetId)?.Name);
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
