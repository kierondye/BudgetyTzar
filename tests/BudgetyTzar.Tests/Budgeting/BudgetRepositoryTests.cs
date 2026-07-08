using System.Security.Claims;
using BudgetyTzar.Api.Authentication;
using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Tests.Budgeting;

public sealed class BudgetRepositoryTests
{
    private static readonly ApplicationUserId TestUser =
        ApplicationUserId.FromPrincipal(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test-user")])),
            "sub");

    [Fact]
    public void Entity_state_exposes_only_the_loaded_value_and_update_operation()
    {
        var entityStateType = typeof(EntityState<Budget>);

        Assert.True(entityStateType.IsAbstract);
        Assert.Empty(entityStateType.GetConstructors());
        var valueProperty = Assert.Single(entityStateType.GetProperties());
        Assert.Equal(nameof(EntityState<Budget>.Value), valueProperty.Name);
        Assert.Null(entityStateType.GetProperty("Version"));
    }

    [Fact]
    public void Save_rejects_state_created_by_another_repository_implementation()
    {
        var repository = new InMemoryBudgetRepository();
        var budget = CreateBudget("UK", "GBP");

        var result = repository.Save(TestUser, new ForeignEntityState<Budget>(budget));

        Assert.IsType<BudgetSaveResult.InvalidState>(result);
        Assert.Empty(repository.GetAll(TestUser));
    }

    [Fact]
    public void Save_rejects_duplicate_budget_names_without_overwriting_existing_budget()
    {
        var repository = new InMemoryBudgetRepository();
        var firstBudget = CreateBudget("UK", "GBP");
        var duplicateBudget = CreateBudget("UK", "EUR");

        var firstResult = repository.Save(TestUser, firstBudget);
        var duplicateResult = repository.Save(TestUser, duplicateBudget);

        Assert.IsType<BudgetSaveResult.Saved>(firstResult);
        Assert.IsType<BudgetSaveResult.DuplicateName>(duplicateResult);

        var budget = Assert.Single(repository.GetAll(TestUser));
        Assert.Equal(firstBudget.BudgetId, budget.BudgetId);
        Assert.Equal("GBP", budget.Currency.Value);
    }

    [Fact]
    public async Task Save_allows_only_one_budget_to_claim_a_name_when_saves_overlap()
    {
        var repository = new InMemoryBudgetRepository();
        var firstBudget = CreateBudget("Shared", "GBP");
        var duplicateBudget = CreateBudget("Shared", "EUR");
        using var start = new ManualResetEventSlim();

        var firstSave = Task.Run(() =>
        {
            start.Wait();
            return repository.Save(TestUser, firstBudget);
        });
        var duplicateSave = Task.Run(() =>
        {
            start.Wait();
            return repository.Save(TestUser, duplicateBudget);
        });

        start.Set();
        var results = await Task.WhenAll(firstSave, duplicateSave);

        Assert.Equal(1, results.Count(result => result is BudgetSaveResult.Saved));
        Assert.Equal(1, results.Count(result => result is BudgetSaveResult.DuplicateName));
        Assert.Single(repository.GetAll(TestUser));
    }

    [Fact]
    public void Save_updates_the_name_index_when_an_existing_budget_is_renamed()
    {
        var repository = new InMemoryBudgetRepository();
        var budget = CreateBudget("UK", "GBP");
        repository.Save(TestUser, budget);

        var budgetState = repository.Get(TestUser, budget.BudgetId);
        Assert.NotNull(budgetState);

        var renamedBudget = Assert.IsType<RenameBudgetResult.Renamed>(budgetState.Value.Rename(Name("Europe")));
        var renameResult = repository.Save(TestUser, budgetState.Update(renamedBudget.Budget));
        var replacementResult = repository.Save(TestUser, CreateBudget("UK", "GBP"));

        Assert.IsType<BudgetSaveResult.Saved>(renameResult);
        Assert.IsType<BudgetSaveResult.Saved>(replacementResult);
        Assert.True(repository.HasBudgetNamed(TestUser, Name("Europe")));
        Assert.True(repository.HasBudgetNamed(TestUser, Name("UK")));
    }

    [Fact]
    public void Save_rejects_stale_updates_without_overwriting_existing_budget()
    {
        var repository = new InMemoryBudgetRepository();
        var budget = CreateBudget("UK", "GBP");
        repository.Save(TestUser, budget);

        var salaryId = Guid.NewGuid();
        var groceriesId = Guid.NewGuid();
        var firstRead = repository.Get(TestUser, budget.BudgetId);
        var staleRead = repository.Get(TestUser, budget.BudgetId);

        Assert.NotNull(firstRead);
        Assert.NotNull(staleRead);

        var addedSalary = Assert.IsType<AddBudgetItemResult.Added>(
            firstRead.Value.AddBudgetItem(
                salaryId,
                Name("Salary"),
                BudgetItemKind.Funding,
                Money("3000.00")));
        var addedGroceriesFromStaleRead = Assert.IsType<AddBudgetItemResult.Added>(
            staleRead.Value.AddBudgetItem(
                groceriesId,
                Name("Groceries"),
                BudgetItemKind.Consumption,
                Money("400.00")));

        var firstResult = repository.Save(TestUser, firstRead.Update(addedSalary.Budget));
        var staleResult = repository.Save(TestUser, staleRead.Update(addedGroceriesFromStaleRead.Budget));

        Assert.IsType<BudgetSaveResult.Saved>(firstResult);
        Assert.IsType<BudgetSaveResult.StaleState>(staleResult);

        var updatedBudget = repository.Get(TestUser, budget.BudgetId);
        Assert.NotNull(updatedBudget);
        var budgetItem = Assert.Single(updatedBudget.Value.BudgetItems);
        Assert.Equal(salaryId, budgetItem.BudgetItemId);
    }

    [Fact]
    public void Save_can_report_conflict_without_changing_stored_budget()
    {
        var repository = new InMemoryBudgetRepository();
        var ukBudget = CreateBudget("UK", "GBP");
        var euBudget = CreateBudget("EU", "EUR");
        repository.Save(TestUser, ukBudget);
        repository.Save(TestUser, euBudget);

        var ukBudgetState = repository.Get(TestUser, ukBudget.BudgetId);
        Assert.NotNull(ukBudgetState);

        var renamedBudget = Assert.IsType<RenameBudgetResult.Renamed>(ukBudgetState.Value.Rename(Name("EU")));
        var result = repository.Save(TestUser, ukBudgetState.Update(renamedBudget.Budget));

        Assert.IsType<BudgetSaveResult.DuplicateName>(result);
        Assert.Equal("UK", repository.Get(TestUser, ukBudget.BudgetId)?.Value.Name.Value);
    }

    private static Budget CreateBudget(string name, string currency)
    {
        return Assert.IsType<CreateBudgetResult.Created>(
            Budget.Create(Guid.NewGuid(), Name(name), Currency(currency))).Budget;
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

    private sealed class ForeignEntityState<T>(T value) : EntityState<T>(value)
    {
        public override EntityState<T> Update(T value)
        {
            return new ForeignEntityState<T>(value);
        }
    }
}
