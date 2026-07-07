using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Tests.Budgeting;

public sealed class BudgetRepositoryTests
{
    [Fact]
    public void Save_rejects_duplicate_budget_names_without_overwriting_existing_budget()
    {
        var repository = new InMemoryBudgetRepository();
        var firstBudget = Budget.Create(Guid.NewGuid(), "UK", Currency("GBP"));
        var duplicateBudget = Budget.Create(Guid.NewGuid(), "UK", Currency("EUR"));

        var firstResult = repository.Save(firstBudget);
        var duplicateResult = repository.Save(duplicateBudget);

        Assert.IsType<BudgetSaveResult.Saved>(firstResult);
        Assert.IsType<BudgetSaveResult.Conflict>(duplicateResult);

        var budget = Assert.Single(repository.GetAll());
        Assert.Equal(firstBudget.BudgetId, budget.BudgetId);
        Assert.Equal("GBP", budget.Currency.Value);
    }

    [Fact]
    public async Task Save_allows_only_one_budget_to_claim_a_name_when_saves_overlap()
    {
        var repository = new InMemoryBudgetRepository();
        var firstBudget = Budget.Create(Guid.NewGuid(), "Shared", Currency("GBP"));
        var duplicateBudget = Budget.Create(Guid.NewGuid(), "Shared", Currency("EUR"));
        using var start = new ManualResetEventSlim();

        var firstSave = Task.Run(() =>
        {
            start.Wait();
            return repository.Save(firstBudget);
        });
        var duplicateSave = Task.Run(() =>
        {
            start.Wait();
            return repository.Save(duplicateBudget);
        });

        start.Set();
        var results = await Task.WhenAll(firstSave, duplicateSave);

        Assert.Equal(1, results.Count(result => result is BudgetSaveResult.Saved));
        Assert.Equal(1, results.Count(result => result is BudgetSaveResult.Conflict));
        Assert.Single(repository.GetAll());
    }

    [Fact]
    public void Save_updates_the_name_index_when_an_existing_budget_is_renamed()
    {
        var repository = new InMemoryBudgetRepository();
        var budget = Budget.Create(Guid.NewGuid(), "UK", Currency("GBP"));
        repository.Save(budget);

        var renamedBudget = budget.Rename("Europe");
        var renameResult = repository.Save(renamedBudget);
        var replacementResult = repository.Save(Budget.Create(Guid.NewGuid(), "UK", Currency("GBP")));

        Assert.IsType<BudgetSaveResult.Saved>(renameResult);
        Assert.IsType<BudgetSaveResult.Saved>(replacementResult);
        Assert.True(repository.HasBudgetNamed("Europe"));
        Assert.True(repository.HasBudgetNamed("UK"));
    }

    [Fact]
    public void TryUpdate_applies_each_update_to_the_latest_stored_budget()
    {
        var repository = new InMemoryBudgetRepository();
        var budget = Budget.Create(Guid.NewGuid(), "UK", Currency("GBP"));
        repository.Save(budget);

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
    public void Save_can_report_conflict_without_changing_stored_budget()
    {
        var repository = new InMemoryBudgetRepository();
        var ukBudget = Budget.Create(Guid.NewGuid(), "UK", Currency("GBP"));
        var euBudget = Budget.Create(Guid.NewGuid(), "EU", Currency("EUR"));
        repository.Save(ukBudget);
        repository.Save(euBudget);

        var result = repository.Save(ukBudget.Rename("EU"));

        Assert.IsType<BudgetSaveResult.Conflict>(result);
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
