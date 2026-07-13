using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Tests.Support.Persistence;

public abstract class TransactionRepositoryContractTests : RepositoryContractTestBase
{
    [Fact]
    public async Task Adapter_contract_get_all_returns_only_current_users_transactions_in_recording_order()
    {
        await using var context = await CreateContextAsync();
        var userA = context.ForUser("repository-test-user-a");
        var userB = context.ForUser("repository-test-user-b");
        var firstTransaction = CreateTransaction("Groceries", "42.50");
        var otherUserTransaction = CreateTransaction("Coffee", "3.50");
        var secondTransaction = CreateTransaction("Fuel", "60.00");

        userA.Transactions.Add(firstTransaction);
        userB.Transactions.Add(otherUserTransaction);
        userA.Transactions.Add(secondTransaction);

        Assert.Equal(
            [firstTransaction.TransactionId, secondTransaction.TransactionId],
            userA.Transactions.GetAll().Select(transaction => transaction.TransactionId));
        Assert.Equal(
            [otherUserTransaction.TransactionId],
            userB.Transactions.GetAll().Select(transaction => transaction.TransactionId));
    }

    [Fact]
    public async Task Adapter_contract_get_returns_non_disclosing_miss_for_missing_or_other_users_transactions()
    {
        await using var context = await CreateContextAsync();
        var userA = context.ForUser("repository-test-user-a");
        var userB = context.ForUser("repository-test-user-b");
        var transaction = CreateTransaction();
        userA.Transactions.Add(transaction);

        Assert.Null(userA.Transactions.Get(Guid.NewGuid()));
        Assert.Null(userB.Transactions.Get(transaction.TransactionId));
    }

    [Fact]
    public async Task Adapter_contract_delete_returns_not_found_for_missing_or_other_users_transactions()
    {
        await using var context = await CreateContextAsync();
        var userA = context.ForUser("repository-test-user-a");
        var userB = context.ForUser("repository-test-user-b");
        var transaction = CreateTransaction();
        userA.Transactions.Add(transaction);

        var missingResult = userA.Transactions.Delete(Guid.NewGuid());
        var otherUserResult = userB.Transactions.Delete(transaction.TransactionId);

        Assert.IsType<TransactionDeleteResult.NotFound>(missingResult);
        Assert.IsType<TransactionDeleteResult.NotFound>(otherUserResult);
        Assert.NotNull(userA.Transactions.Get(transaction.TransactionId));
    }

    [Fact]
    public async Task Adapter_contract_delete_removes_unallocated_transactions()
    {
        await using var context = await CreateContextAsync();
        var repositories = context.ForUser("repository-test-user");
        var transaction = CreateTransaction();
        repositories.Transactions.Add(transaction);

        var result = repositories.Transactions.Delete(transaction.TransactionId);

        Assert.IsType<TransactionDeleteResult.Deleted>(result);
        Assert.Null(repositories.Transactions.Get(transaction.TransactionId));
        Assert.Empty(repositories.Transactions.GetAll());
    }

    [Fact]
    public async Task Adapter_contract_delete_rejects_allocated_transactions_under_the_shared_persistence_boundary()
    {
        await using var context = await CreateContextAsync();
        var repositories = context.ForUser("repository-test-user");
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();

        repositories.Transactions.Add(transaction);
        repositories.Budgets.Save(CreateBudget((budgetItemId, "Groceries")));
        repositories.Allocations.Allocate(CreateAllocation(transaction, budgetItemId));

        var deleteResult = repositories.Transactions.Delete(transaction.TransactionId);

        Assert.IsType<TransactionDeleteResult.TransactionHasAllocation>(deleteResult);
        Assert.NotNull(repositories.Transactions.Get(transaction.TransactionId));
        Assert.Equal(budgetItemId, repositories.Allocations.Get(transaction.TransactionId)?.BudgetItemId);
    }
}
