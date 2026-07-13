using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Identity;

namespace BudgetyTzar.Tests.Budgeting;

public sealed class BudgetRepositoryTests
{
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
        var repository = CreateRepository();
        var budget = CreateBudget("UK", "GBP");

        var result = repository.Save(new ForeignEntityState<Budget>(budget));

        Assert.IsType<BudgetSaveResult.InvalidState>(result);
        Assert.Empty(repository.GetAll());
    }

    [Fact]
    public void Save_rejects_duplicate_budget_names_without_overwriting_existing_budget()
    {
        var repository = CreateRepository();
        var firstBudget = CreateBudget("UK", "GBP");
        var duplicateBudget = CreateBudget("UK", "EUR");

        var firstResult = repository.Save(firstBudget);
        var duplicateResult = repository.Save(duplicateBudget);

        Assert.IsType<BudgetSaveResult.Saved>(firstResult);
        Assert.IsType<BudgetSaveResult.DuplicateName>(duplicateResult);

        var budget = Assert.Single(repository.GetAll());
        Assert.Equal(firstBudget.BudgetId, budget.BudgetId);
        Assert.Equal("GBP", budget.Currency.Value);
    }

    [Fact]
    public void Save_rejects_duplicate_budget_identities_without_overwriting_existing_budget()
    {
        var repository = CreateRepository();
        var budget = CreateBudget("UK", "GBP");

        var firstResult = repository.Save(budget);
        var duplicateResult = repository.Save(budget);

        Assert.IsType<BudgetSaveResult.Saved>(firstResult);
        Assert.IsType<BudgetSaveResult.DuplicateIdentity>(duplicateResult);

        var storedBudget = Assert.Single(repository.GetAll());
        Assert.Equal(budget.BudgetId, storedBudget.BudgetId);
    }

    [Fact]
    public void Get_all_returns_budgets_in_creation_order()
    {
        var repository = CreateRepository();
        var firstBudget = CreateBudget("UK", "GBP");
        var secondBudget = CreateBudget("EU", "EUR");

        repository.Save(firstBudget);
        repository.Save(secondBudget);

        Assert.Equal(
            [firstBudget.BudgetId, secondBudget.BudgetId],
            repository.GetAll().Select(budget => budget.BudgetId));
    }

    [Fact]
    public async Task Save_allows_only_one_budget_to_claim_a_name_when_saves_overlap()
    {
        var repository = CreateRepository();
        var firstBudget = CreateBudget("Shared", "GBP");
        var duplicateBudget = CreateBudget("Shared", "EUR");
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
        Assert.Equal(1, results.Count(result => result is BudgetSaveResult.DuplicateName));
        Assert.Single(repository.GetAll());
    }

    [Fact]
    public void Save_updates_the_name_index_when_an_existing_budget_is_renamed()
    {
        var repository = CreateRepository();
        var budget = CreateBudget("UK", "GBP");
        repository.Save(budget);

        var budgetState = repository.Get(budget.BudgetId);
        Assert.NotNull(budgetState);

        var renamedBudget = Assert.IsType<RenameBudgetResult.Renamed>(budgetState.Value.Rename(Name("Europe")));
        var renameResult = repository.Save(budgetState.Update(renamedBudget.Budget));
        var replacementResult = repository.Save(CreateBudget("UK", "GBP"));

        Assert.IsType<BudgetSaveResult.Saved>(renameResult);
        Assert.IsType<BudgetSaveResult.Saved>(replacementResult);
        Assert.True(repository.HasBudgetNamed(Name("Europe")));
        Assert.True(repository.HasBudgetNamed(Name("UK")));
    }

    [Fact]
    public void Save_rejects_stale_updates_without_overwriting_existing_budget()
    {
        var repository = CreateRepository();
        var budget = CreateBudget("UK", "GBP");
        repository.Save(budget);

        var salaryId = Guid.NewGuid();
        var groceriesId = Guid.NewGuid();
        var firstRead = repository.Get(budget.BudgetId);
        var staleRead = repository.Get(budget.BudgetId);

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

        var firstResult = repository.Save(firstRead.Update(addedSalary.Budget));
        var staleResult = repository.Save(staleRead.Update(addedGroceriesFromStaleRead.Budget));

        Assert.IsType<BudgetSaveResult.Saved>(firstResult);
        Assert.IsType<BudgetSaveResult.StaleState>(staleResult);

        var updatedBudget = repository.Get(budget.BudgetId);
        Assert.NotNull(updatedBudget);
        var budgetItem = Assert.Single(updatedBudget.Value.BudgetItems);
        Assert.Equal(salaryId, budgetItem.BudgetItemId);
    }

    [Fact]
    public void Save_rejects_updated_state_when_the_budget_is_missing()
    {
        var repository = CreateRepository();
        var otherRepository = CreateRepository();
        var budget = CreateBudget("UK", "GBP");
        otherRepository.Save(budget);

        var otherRepositoryState = otherRepository.Get(budget.BudgetId);
        Assert.NotNull(otherRepositoryState);

        var renamed = Assert.IsType<RenameBudgetResult.Renamed>(
            otherRepositoryState.Value.Rename(Name("UK 2026")));

        var result = repository.Save(otherRepositoryState.Update(renamed.Budget));

        Assert.IsType<BudgetSaveResult.NotFound>(result);
        Assert.Empty(repository.GetAll());
    }

    [Fact]
    public void Save_can_report_conflict_without_changing_stored_budget()
    {
        var repository = CreateRepository();
        var ukBudget = CreateBudget("UK", "GBP");
        var euBudget = CreateBudget("EU", "EUR");
        repository.Save(ukBudget);
        repository.Save(euBudget);

        var ukBudgetState = repository.Get(ukBudget.BudgetId);
        Assert.NotNull(ukBudgetState);

        var renamedBudget = Assert.IsType<RenameBudgetResult.Renamed>(ukBudgetState.Value.Rename(Name("EU")));
        var result = repository.Save(ukBudgetState.Update(renamedBudget.Budget));

        Assert.IsType<BudgetSaveResult.DuplicateName>(result);
        Assert.Equal("UK", repository.Get(ukBudget.BudgetId)?.Value.Name.Value);
    }

    private static Budget CreateBudget(string name, string currency)
    {
        return Assert.IsType<CreateBudgetResult.Created>(
            Budget.Create(Guid.NewGuid(), Name(name), Currency(currency))).Budget;
    }

    private static IBudgetRepository CreateRepository()
    {
        return new InMemoryBudgetRepository(new InMemoryDataStore(), CurrentUser("repository-test-user"));
    }

    private static CurrentUser CurrentUser(string value)
    {
        return ExternalIdentity.TryCreate("BudgetyTzar.Tests", value, out var externalIdentity)
            ? new CurrentUser(new InMemoryApplicationUserStore()
                .GetOrCreateApplicationUserId(ApplicationUserKey.FromExternalIdentity(externalIdentity!)))
            : throw new InvalidOperationException("Invalid test user.");
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
