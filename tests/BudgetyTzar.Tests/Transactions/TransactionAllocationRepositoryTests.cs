using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Tests.Transactions;

public sealed class TransactionAllocationRepositoryTests
{
    [Fact]
    public void Allocate_same_transaction_to_same_budget_item_is_idempotent()
    {
        var store = new InMemoryDataStore();
        var currentUser = CurrentUser("repository-test-user");
        IBudgetRepository budgetRepository = new InMemoryBudgetRepository(store, currentUser);
        ITransactionRepository transactionRepository = new InMemoryTransactionRepository(store, currentUser);
        ITransactionAllocationRepository repository = new InMemoryTransactionAllocationRepository(store, currentUser);
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        transactionRepository.Add(transaction);
        budgetRepository.Save(CreateBudget((budgetItemId, "Groceries")));
        var firstAllocation = CreateAllocation(transaction, budgetItemId);
        var secondAllocation = CreateAllocation(transaction, budgetItemId);

        var firstResult = repository.Allocate(firstAllocation);
        var secondResult = repository.Allocate(secondAllocation);

        var firstAllocated = Assert.IsType<AllocateTransactionResult.Allocated>(firstResult);
        var secondAllocated = Assert.IsType<AllocateTransactionResult.Allocated>(secondResult);
        Assert.Same(firstAllocated.Allocation, secondAllocated.Allocation);
        Assert.Equal(budgetItemId, repository.Get(transaction.TransactionId)?.BudgetItemId);
    }

    [Fact]
    public void Allocate_same_transaction_to_different_budget_item_conflicts_and_preserves_first_allocation()
    {
        var store = new InMemoryDataStore();
        var currentUser = CurrentUser("repository-test-user");
        IBudgetRepository budgetRepository = new InMemoryBudgetRepository(store, currentUser);
        ITransactionRepository transactionRepository = new InMemoryTransactionRepository(store, currentUser);
        ITransactionAllocationRepository repository = new InMemoryTransactionAllocationRepository(store, currentUser);
        var transaction = CreateTransaction();
        var firstBudgetItemId = Guid.NewGuid();
        var secondBudgetItemId = Guid.NewGuid();
        transactionRepository.Add(transaction);
        budgetRepository.Save(CreateBudget(
            (firstBudgetItemId, "Groceries"),
            (secondBudgetItemId, "Restaurants")));

        var firstResult = repository.Allocate(CreateAllocation(transaction, firstBudgetItemId));
        var secondResult = repository.Allocate(CreateAllocation(transaction, secondBudgetItemId));

        Assert.IsType<AllocateTransactionResult.Allocated>(firstResult);
        Assert.IsType<AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem>(secondResult);
        Assert.Equal(firstBudgetItemId, repository.Get(transaction.TransactionId)?.BudgetItemId);
    }

    [Fact]
    public void Allocate_revalidates_budget_item_exists_under_the_shared_persistence_boundary()
    {
        var store = new InMemoryDataStore();
        var currentUser = CurrentUser("repository-test-user");
        IBudgetRepository budgetRepository = new InMemoryBudgetRepository(store, currentUser);
        ITransactionRepository transactionRepository = new InMemoryTransactionRepository(store, currentUser);
        ITransactionAllocationRepository allocationRepository = new InMemoryTransactionAllocationRepository(store, currentUser);
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        var budget = CreateBudget((budgetItemId, "Groceries"));

        transactionRepository.Add(transaction);
        budgetRepository.Save(budget);
        var budgetState = budgetRepository.Get(budget.BudgetId);
        Assert.NotNull(budgetState);

        var removed = Assert.IsType<RemoveBudgetItemResult.Removed>(
            budgetState.Value.RemoveBudgetItem(budgetItemId));
        var removeResult = budgetRepository.Save(budgetState.Update(removed.Budget));

        var allocateResult = allocationRepository.Allocate(CreateAllocation(transaction, budgetItemId));

        Assert.IsType<BudgetSaveResult.Saved>(removeResult);
        Assert.IsType<AllocateTransactionResult.BudgetItemNotFound>(allocateResult);
        Assert.Null(allocationRepository.Get(transaction.TransactionId));
    }

    [Fact]
    public void Save_rejects_deleted_budget_items_with_allocations_under_the_shared_persistence_boundary()
    {
        var store = new InMemoryDataStore();
        var currentUser = CurrentUser("repository-test-user");
        IBudgetRepository budgetRepository = new InMemoryBudgetRepository(store, currentUser);
        ITransactionRepository transactionRepository = new InMemoryTransactionRepository(store, currentUser);
        ITransactionAllocationRepository allocationRepository = new InMemoryTransactionAllocationRepository(store, currentUser);
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        var budget = CreateBudget((budgetItemId, "Groceries"));

        transactionRepository.Add(transaction);
        budgetRepository.Save(budget);
        var budgetState = budgetRepository.Get(budget.BudgetId);
        Assert.NotNull(budgetState);

        var allocateResult = allocationRepository.Allocate(CreateAllocation(transaction, budgetItemId));

        var removed = Assert.IsType<RemoveBudgetItemResult.Removed>(
            budgetState.Value.RemoveBudgetItem(budgetItemId));
        var removeResult = budgetRepository.Save(budgetState.Update(removed.Budget));

        Assert.IsType<AllocateTransactionResult.Allocated>(allocateResult);
        Assert.IsType<BudgetSaveResult.BudgetItemHasAllocations>(removeResult);
        Assert.NotNull(budgetRepository.GetBudgetItemReference(budgetItemId));
        Assert.Equal(budgetItemId, allocationRepository.Get(transaction.TransactionId)?.BudgetItemId);
    }

    [Fact]
    public void Delete_transaction_revalidates_transaction_has_no_allocation_under_the_shared_persistence_boundary()
    {
        var store = new InMemoryDataStore();
        var currentUser = CurrentUser("repository-test-user");
        IBudgetRepository budgetRepository = new InMemoryBudgetRepository(store, currentUser);
        ITransactionRepository transactionRepository = new InMemoryTransactionRepository(store, currentUser);
        ITransactionAllocationRepository allocationRepository = new InMemoryTransactionAllocationRepository(store, currentUser);
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();

        transactionRepository.Add(transaction);
        budgetRepository.Save(CreateBudget((budgetItemId, "Groceries")));
        allocationRepository.Allocate(CreateAllocation(transaction, budgetItemId));

        var deleteResult = transactionRepository.Delete(transaction.TransactionId);

        Assert.IsType<TransactionDeleteResult.TransactionHasAllocation>(deleteResult);
        Assert.NotNull(transactionRepository.Get(transaction.TransactionId));
        Assert.Equal(budgetItemId, allocationRepository.Get(transaction.TransactionId)?.BudgetItemId);
    }

    [Fact]
    public void Allocate_rejects_missing_transactions_without_saving_an_allocation()
    {
        var store = new InMemoryDataStore();
        var currentUser = CurrentUser("repository-test-user");
        IBudgetRepository budgetRepository = new InMemoryBudgetRepository(store, currentUser);
        ITransactionAllocationRepository allocationRepository = new InMemoryTransactionAllocationRepository(store, currentUser);
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        budgetRepository.Save(CreateBudget((budgetItemId, "Groceries")));

        var allocateResult = allocationRepository.Allocate(CreateAllocation(transaction, budgetItemId));

        Assert.IsType<AllocateTransactionResult.TransactionNotFound>(allocateResult);
        Assert.Null(allocationRepository.Get(transaction.TransactionId));
        Assert.Empty(allocationRepository.GetAll());
    }

    [Fact]
    public void Remove_deletes_only_the_current_users_allocation()
    {
        var store = new InMemoryDataStore();
        var currentUser = CurrentUser("repository-test-user");
        var otherUser = CurrentUser("other-repository-test-user");
        IBudgetRepository budgetRepository = new InMemoryBudgetRepository(store, currentUser);
        ITransactionRepository transactionRepository = new InMemoryTransactionRepository(store, currentUser);
        ITransactionAllocationRepository allocationRepository = new InMemoryTransactionAllocationRepository(store, currentUser);
        IBudgetRepository otherBudgetRepository = new InMemoryBudgetRepository(store, otherUser);
        ITransactionRepository otherTransactionRepository = new InMemoryTransactionRepository(store, otherUser);
        ITransactionAllocationRepository otherAllocationRepository = new InMemoryTransactionAllocationRepository(store, otherUser);
        var transaction = CreateTransaction();
        var otherTransaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        var otherBudgetItemId = Guid.NewGuid();
        transactionRepository.Add(transaction);
        otherTransactionRepository.Add(otherTransaction);
        budgetRepository.Save(CreateBudget((budgetItemId, "Groceries")));
        otherBudgetRepository.Save(CreateBudget((otherBudgetItemId, "Groceries")));
        allocationRepository.Allocate(CreateAllocation(transaction, budgetItemId));
        otherAllocationRepository.Allocate(CreateAllocation(otherTransaction, otherBudgetItemId));

        allocationRepository.Remove(transaction.TransactionId);
        allocationRepository.Remove(otherTransaction.TransactionId);

        Assert.Null(allocationRepository.Get(transaction.TransactionId));
        Assert.Equal(otherBudgetItemId, otherAllocationRepository.Get(otherTransaction.TransactionId)?.BudgetItemId);
    }

    private static Transaction CreateTransaction()
    {
        return Assert.IsType<RecordTransactionResult.Recorded>(
            Transaction.Record(
                Guid.NewGuid(),
                "Groceries",
                TransactionType.Debit,
                new DateOnly(2026, 7, 2),
                Money("42.50"),
                Currency("GBP"))).Transaction;
    }

    private static CurrentUser CurrentUser(string value)
    {
        return ApplicationUserId.TryCreate(value, out var userId)
            ? new CurrentUser(userId!)
            : throw new InvalidOperationException("Invalid test user.");
    }

    private static TransactionAllocation CreateAllocation(Transaction transaction, Guid budgetItemId)
    {
        return Assert.IsType<AllocateTransactionEntityResult.Allocated>(
            TransactionAllocation.Allocate(transaction, budgetItemId)).Allocation;
    }

    private static Budget CreateBudget(params (Guid BudgetItemId, string Name)[] budgetItems)
    {
        var budget = Assert.IsType<CreateBudgetResult.Created>(
            Budget.Create(Guid.NewGuid(), Name("UK"), Currency("GBP"))).Budget;

        foreach (var budgetItem in budgetItems)
        {
            budget = Assert.IsType<AddBudgetItemResult.Added>(
                budget.AddBudgetItem(
                    budgetItem.BudgetItemId,
                    Name(budgetItem.Name),
                    BudgetItemKind.Consumption,
                    Money("400.00"))).Budget;
        }

        return budget;
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
