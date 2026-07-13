using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Tests.Support.Persistence;

public abstract class TransactionAllocationRepositoryContractTests : RepositoryContractTestBase
{
    [Fact]
    public async Task Adapter_contract_allocate_same_transaction_to_same_budget_item_is_idempotent()
    {
        await using var context = await CreateContextAsync();
        var repositories = context.ForUser("repository-test-user");
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        repositories.Transactions.Add(transaction);
        repositories.Budgets.Save(CreateBudget((budgetItemId, "Groceries")));
        var firstAllocation = CreateAllocation(transaction, budgetItemId);
        var secondAllocation = CreateAllocation(transaction, budgetItemId);

        var firstResult = repositories.Allocations.Allocate(firstAllocation);
        var secondResult = repositories.Allocations.Allocate(secondAllocation);

        var firstAllocated = Assert.IsType<AllocateTransactionResult.Allocated>(firstResult);
        var secondAllocated = Assert.IsType<AllocateTransactionResult.Allocated>(secondResult);
        Assert.Equal(firstAllocated.Allocation.TransactionId, secondAllocated.Allocation.TransactionId);
        Assert.Equal(firstAllocated.Allocation.BudgetItemId, secondAllocated.Allocation.BudgetItemId);
        Assert.Equal(budgetItemId, repositories.Allocations.Get(transaction.TransactionId)?.BudgetItemId);
    }

    [Fact]
    public async Task Adapter_contract_allocate_same_transaction_to_different_budget_item_conflicts_and_preserves_first_allocation()
    {
        await using var context = await CreateContextAsync();
        var repositories = context.ForUser("repository-test-user");
        var transaction = CreateTransaction();
        var firstBudgetItemId = Guid.NewGuid();
        var secondBudgetItemId = Guid.NewGuid();
        repositories.Transactions.Add(transaction);
        repositories.Budgets.Save(CreateBudget(
            (firstBudgetItemId, "Groceries"),
            (secondBudgetItemId, "Restaurants")));

        var firstResult = repositories.Allocations.Allocate(CreateAllocation(transaction, firstBudgetItemId));
        var secondResult = repositories.Allocations.Allocate(CreateAllocation(transaction, secondBudgetItemId));

        Assert.IsType<AllocateTransactionResult.Allocated>(firstResult);
        Assert.IsType<AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem>(secondResult);
        Assert.Equal(firstBudgetItemId, repositories.Allocations.Get(transaction.TransactionId)?.BudgetItemId);
    }

    [Fact]
    public async Task Adapter_contract_allocate_revalidates_budget_item_exists_under_the_shared_persistence_boundary()
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

        var removed = Assert.IsType<RemoveBudgetItemResult.Removed>(
            budgetState.Value.RemoveBudgetItem(budgetItemId));
        var removeResult = repositories.Budgets.Save(budgetState.Update(removed.Budget));

        var allocateResult = repositories.Allocations.Allocate(CreateAllocation(transaction, budgetItemId));

        Assert.IsType<BudgetSaveResult.Saved>(removeResult);
        Assert.IsType<AllocateTransactionResult.BudgetItemNotFound>(allocateResult);
        Assert.Null(repositories.Allocations.Get(transaction.TransactionId));
    }

    [Fact]
    public async Task Adapter_contract_allocate_rejects_missing_or_other_users_transactions_without_saving_an_allocation()
    {
        await using var context = await CreateContextAsync();
        var userA = context.ForUser("repository-test-user-a");
        var userB = context.ForUser("repository-test-user-b");
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        userA.Transactions.Add(transaction);
        userB.Budgets.Save(CreateBudget((budgetItemId, "Groceries")));

        var missingResult = userB.Allocations.Allocate(CreateAllocation(CreateTransaction(), budgetItemId));
        var otherUserResult = userB.Allocations.Allocate(CreateAllocation(transaction, budgetItemId));

        Assert.IsType<AllocateTransactionResult.TransactionNotFound>(missingResult);
        Assert.IsType<AllocateTransactionResult.TransactionNotFound>(otherUserResult);
        Assert.Null(userB.Allocations.Get(transaction.TransactionId));
        Assert.Empty(userB.Allocations.GetAll());
    }

    [Fact]
    public async Task Adapter_contract_allocate_rejects_missing_or_other_users_budget_items_without_saving_an_allocation()
    {
        await using var context = await CreateContextAsync();
        var userA = context.ForUser("repository-test-user-a");
        var userB = context.ForUser("repository-test-user-b");
        var transaction = CreateTransaction();
        var userABudgetItemId = Guid.NewGuid();
        userA.Budgets.Save(CreateBudget((userABudgetItemId, "Groceries")));
        userB.Transactions.Add(transaction);

        var missingResult = userB.Allocations.Allocate(CreateAllocation(transaction, Guid.NewGuid()));
        var otherUserResult = userB.Allocations.Allocate(CreateAllocation(transaction, userABudgetItemId));

        Assert.IsType<AllocateTransactionResult.BudgetItemNotFound>(missingResult);
        Assert.IsType<AllocateTransactionResult.BudgetItemNotFound>(otherUserResult);
        Assert.Null(userB.Allocations.Get(transaction.TransactionId));
        Assert.Empty(userB.Allocations.GetAll());
    }

    [Fact]
    public async Task Adapter_contract_get_and_get_all_return_only_current_users_allocations()
    {
        await using var context = await CreateContextAsync();
        var userA = context.ForUser("repository-test-user-a");
        var userB = context.ForUser("repository-test-user-b");
        var userATransaction = CreateTransaction();
        var userBTransaction = CreateTransaction();
        var userABudgetItemId = Guid.NewGuid();
        var userBBudgetItemId = Guid.NewGuid();
        userA.Transactions.Add(userATransaction);
        userB.Transactions.Add(userBTransaction);
        userA.Budgets.Save(CreateBudget((userABudgetItemId, "Groceries")));
        userB.Budgets.Save(CreateBudget((userBBudgetItemId, "Groceries")));
        userA.Allocations.Allocate(CreateAllocation(userATransaction, userABudgetItemId));
        userB.Allocations.Allocate(CreateAllocation(userBTransaction, userBBudgetItemId));

        Assert.Equal(userABudgetItemId, userA.Allocations.Get(userATransaction.TransactionId)?.BudgetItemId);
        Assert.Null(userA.Allocations.Get(userBTransaction.TransactionId));
        Assert.Equal(
            [userATransaction.TransactionId],
            userA.Allocations.GetAll().Select(allocation => allocation.TransactionId));
        Assert.Equal(
            [userBTransaction.TransactionId],
            userB.Allocations.GetAll().Select(allocation => allocation.TransactionId));
    }

    [Fact]
    public async Task Adapter_contract_remove_deletes_only_the_current_users_allocation()
    {
        await using var context = await CreateContextAsync();
        var currentUser = context.ForUser("repository-test-user");
        var otherUser = context.ForUser("other-repository-test-user");
        var transaction = CreateTransaction();
        var otherTransaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        var otherBudgetItemId = Guid.NewGuid();
        currentUser.Transactions.Add(transaction);
        otherUser.Transactions.Add(otherTransaction);
        currentUser.Budgets.Save(CreateBudget((budgetItemId, "Groceries")));
        otherUser.Budgets.Save(CreateBudget((otherBudgetItemId, "Groceries")));
        currentUser.Allocations.Allocate(CreateAllocation(transaction, budgetItemId));
        otherUser.Allocations.Allocate(CreateAllocation(otherTransaction, otherBudgetItemId));

        currentUser.Allocations.Remove(transaction.TransactionId);
        currentUser.Allocations.Remove(otherTransaction.TransactionId);

        Assert.Null(currentUser.Allocations.Get(transaction.TransactionId));
        Assert.Equal(otherBudgetItemId, otherUser.Allocations.Get(otherTransaction.TransactionId)?.BudgetItemId);
    }
}
