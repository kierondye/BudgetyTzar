using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Tests.Support.Persistence;

public abstract class BudgetRepositoryContractTests : RepositoryContractTestBase
{
    [Fact]
    public async Task Adapter_contract_save_rejects_state_created_by_another_repository_implementation()
    {
        await using var context = await CreateContextAsync();
        var repositories = context.ForUser("repository-test-user");
        var budget = CreateBudget();

        var result = repositories.Budgets.Save(new ForeignEntityState<Budget>(budget));

        Assert.IsType<BudgetSaveResult.InvalidState>(result);
        Assert.Empty(repositories.Budgets.GetAll());
    }

    [Fact]
    public async Task Adapter_contract_save_rejects_duplicate_budget_names_without_overwriting_existing_budget()
    {
        await using var context = await CreateContextAsync();
        var repositories = context.ForUser("repository-test-user");
        var firstBudget = CreateBudget("UK", "GBP");
        var duplicateBudget = CreateBudget("UK", "EUR");

        var firstResult = repositories.Budgets.Save(firstBudget);
        var duplicateResult = repositories.Budgets.Save(duplicateBudget);

        Assert.IsType<BudgetSaveResult.Saved>(firstResult);
        Assert.IsType<BudgetSaveResult.DuplicateName>(duplicateResult);

        var budget = Assert.Single(repositories.Budgets.GetAll());
        Assert.Equal(firstBudget.BudgetId, budget.BudgetId);
        Assert.Equal("GBP", budget.Currency.Value);
    }

    [Fact]
    public async Task Adapter_contract_save_rejects_duplicate_budget_identities_without_overwriting_existing_budget()
    {
        await using var context = await CreateContextAsync();
        var repositories = context.ForUser("repository-test-user");
        var budget = CreateBudget();

        var firstResult = repositories.Budgets.Save(budget);
        var duplicateResult = repositories.Budgets.Save(budget);

        Assert.IsType<BudgetSaveResult.Saved>(firstResult);
        Assert.IsType<BudgetSaveResult.DuplicateIdentity>(duplicateResult);

        var storedBudget = Assert.Single(repositories.Budgets.GetAll());
        Assert.Equal(budget.BudgetId, storedBudget.BudgetId);
    }

    [Fact]
    public async Task Adapter_contract_get_all_returns_only_current_users_budgets_in_creation_order()
    {
        await using var context = await CreateContextAsync();
        var userA = context.ForUser("repository-test-user-a");
        var userB = context.ForUser("repository-test-user-b");
        var firstBudget = CreateBudget("UK", "GBP");
        var otherUserBudget = CreateBudget("Shared", "GBP");
        var secondBudget = CreateBudget("EU", "EUR");

        userA.Budgets.Save(firstBudget);
        userB.Budgets.Save(otherUserBudget);
        userA.Budgets.Save(secondBudget);

        Assert.Equal(
            [firstBudget.BudgetId, secondBudget.BudgetId],
            userA.Budgets.GetAll().Select(budget => budget.BudgetId));
        Assert.Equal([otherUserBudget.BudgetId], userB.Budgets.GetAll().Select(budget => budget.BudgetId));
    }

    [Fact]
    public async Task Adapter_contract_budget_names_are_unique_per_current_user()
    {
        await using var context = await CreateContextAsync();
        var userA = context.ForUser("repository-test-user-a");
        var userB = context.ForUser("repository-test-user-b");
        var userABudget = CreateBudget("Shared", "GBP");
        var userBBudget = CreateBudget("Shared", "EUR");

        var userAResult = userA.Budgets.Save(userABudget);
        var userBResult = userB.Budgets.Save(userBBudget);

        Assert.IsType<BudgetSaveResult.Saved>(userAResult);
        Assert.IsType<BudgetSaveResult.Saved>(userBResult);
        Assert.True(userA.Budgets.HasBudgetNamed(Name("Shared")));
        Assert.True(userB.Budgets.HasBudgetNamed(Name("Shared")));
    }

    [Fact]
    public async Task Adapter_contract_get_and_budget_item_lookups_return_non_disclosing_misses_for_other_users_resources()
    {
        await using var context = await CreateContextAsync();
        var userA = context.ForUser("repository-test-user-a");
        var userB = context.ForUser("repository-test-user-b");
        var budgetItemId = Guid.NewGuid();
        var budget = CreateBudget((budgetItemId, "Groceries"));
        userA.Budgets.Save(budget);

        Assert.Null(userB.Budgets.Get(budget.BudgetId));
        Assert.Null(userB.Budgets.GetBudgetItem(budget.BudgetId, budgetItemId));
        Assert.Null(userB.Budgets.GetBudgetItemReference(budgetItemId));
    }

    [Fact]
    public async Task Adapter_contract_save_updates_the_name_index_when_an_existing_budget_is_renamed()
    {
        await using var context = await CreateContextAsync();
        var repositories = context.ForUser("repository-test-user");
        var budget = CreateBudget("UK", "GBP");
        repositories.Budgets.Save(budget);

        var budgetState = repositories.Budgets.Get(budget.BudgetId);
        Assert.NotNull(budgetState);

        var renamedBudget = Assert.IsType<RenameBudgetResult.Renamed>(budgetState.Value.Rename(Name("Europe")));
        var renameResult = repositories.Budgets.Save(budgetState.Update(renamedBudget.Budget));
        var replacementResult = repositories.Budgets.Save(CreateBudget("UK", "GBP"));

        Assert.IsType<BudgetSaveResult.Saved>(renameResult);
        Assert.IsType<BudgetSaveResult.Saved>(replacementResult);
        Assert.True(repositories.Budgets.HasBudgetNamed(Name("Europe")));
        Assert.True(repositories.Budgets.HasBudgetNamed(Name("UK")));
    }

    [Fact]
    public async Task Adapter_contract_save_rejects_stale_updates_without_overwriting_existing_budget()
    {
        await using var context = await CreateContextAsync();
        var repositories = context.ForUser("repository-test-user");
        var budget = CreateBudget("UK", "GBP");
        repositories.Budgets.Save(budget);

        var salaryId = Guid.NewGuid();
        var groceriesId = Guid.NewGuid();
        var firstRead = repositories.Budgets.Get(budget.BudgetId);
        var staleRead = repositories.Budgets.Get(budget.BudgetId);

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

        var firstResult = repositories.Budgets.Save(firstRead.Update(addedSalary.Budget));
        var staleResult = repositories.Budgets.Save(staleRead.Update(addedGroceriesFromStaleRead.Budget));

        Assert.IsType<BudgetSaveResult.Saved>(firstResult);
        Assert.IsType<BudgetSaveResult.StaleState>(staleResult);

        var updatedBudget = repositories.Budgets.Get(budget.BudgetId);
        Assert.NotNull(updatedBudget);
        var budgetItem = Assert.Single(updatedBudget.Value.BudgetItems);
        Assert.Equal(salaryId, budgetItem.BudgetItemId);
    }

    [Fact]
    public async Task Adapter_contract_save_rejects_updated_state_when_the_budget_is_missing_or_owned_by_another_user()
    {
        await using var context = await CreateContextAsync();
        var userA = context.ForUser("repository-test-user-a");
        var userB = context.ForUser("repository-test-user-b");
        var budget = CreateBudget("UK", "GBP");
        userA.Budgets.Save(budget);

        var userAState = userA.Budgets.Get(budget.BudgetId);
        Assert.NotNull(userAState);

        var renamed = Assert.IsType<RenameBudgetResult.Renamed>(
            userAState.Value.Rename(Name("UK 2026")));

        var result = userB.Budgets.Save(userAState.Update(renamed.Budget));

        Assert.IsType<BudgetSaveResult.NotFound>(result);
        Assert.Null(userB.Budgets.Get(budget.BudgetId));
        Assert.Equal("UK", userA.Budgets.Get(budget.BudgetId)?.Value.Name.Value);
    }

    [Fact]
    public async Task Adapter_contract_save_can_report_conflict_without_changing_stored_budget()
    {
        await using var context = await CreateContextAsync();
        var repositories = context.ForUser("repository-test-user");
        var ukBudget = CreateBudget("UK", "GBP");
        var euBudget = CreateBudget("EU", "EUR");
        repositories.Budgets.Save(ukBudget);
        repositories.Budgets.Save(euBudget);

        var ukBudgetState = repositories.Budgets.Get(ukBudget.BudgetId);
        Assert.NotNull(ukBudgetState);

        var renamedBudget = Assert.IsType<RenameBudgetResult.Renamed>(ukBudgetState.Value.Rename(Name("EU")));
        var result = repositories.Budgets.Save(ukBudgetState.Update(renamedBudget.Budget));

        Assert.IsType<BudgetSaveResult.DuplicateName>(result);
        Assert.Equal("UK", repositories.Budgets.Get(ukBudget.BudgetId)?.Value.Name.Value);
    }

    [Fact]
    public async Task Adapter_contract_save_rejects_deleted_budget_items_with_allocations_under_the_shared_persistence_boundary()
    {
        await using var context = await CreateContextAsync();
        var repositories = context.ForUser("repository-test-user");
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        var budget = CreateBudget((budgetItemId, "Groceries"));

        repositories.Transactions.Add(transaction);
        repositories.Budgets.Save(budget);
        var budgetState = repositories.Budgets.Get(budget.BudgetId);
        Assert.NotNull(budgetState);

        var allocateResult = repositories.Allocations.Allocate(CreateAllocation(transaction, budgetItemId));

        var removed = Assert.IsType<RemoveBudgetItemResult.Removed>(
            budgetState.Value.RemoveBudgetItem(budgetItemId));
        var removeResult = repositories.Budgets.Save(budgetState.Update(removed.Budget));

        Assert.IsType<AllocateTransactionResult.Allocated>(allocateResult);
        Assert.IsType<BudgetSaveResult.BudgetItemHasAllocations>(removeResult);
        Assert.NotNull(repositories.Budgets.GetBudgetItemReference(budgetItemId));
        Assert.Equal(budgetItemId, repositories.Allocations.Get(transaction.TransactionId)?.BudgetItemId);
    }

    private sealed class ForeignEntityState<T>(T value) : EntityState<T>(value)
    {
        public override EntityState<T> Update(T value)
        {
            return new ForeignEntityState<T>(value);
        }
    }
}
