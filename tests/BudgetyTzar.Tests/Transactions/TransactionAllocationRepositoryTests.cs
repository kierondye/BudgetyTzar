using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Tests.Transactions;

public sealed class TransactionAllocationRepositoryTests
{
    [Fact]
    public void Allocate_same_transaction_to_same_budget_item_is_idempotent()
    {
        var repository = new InMemoryTransactionAllocationRepository();
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
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
        var repository = new InMemoryTransactionAllocationRepository();
        var transaction = CreateTransaction();
        var firstBudgetItemId = Guid.NewGuid();
        var secondBudgetItemId = Guid.NewGuid();

        var firstResult = repository.Allocate(CreateAllocation(transaction, firstBudgetItemId));
        var secondResult = repository.Allocate(CreateAllocation(transaction, secondBudgetItemId));

        Assert.IsType<AllocateTransactionResult.Allocated>(firstResult);
        Assert.IsType<AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem>(secondResult);
        Assert.Equal(firstBudgetItemId, repository.Get(transaction.TransactionId)?.BudgetItemId);
    }

    [Fact]
    public void Allocate_revalidates_budget_item_exists_under_the_shared_persistence_lock()
    {
        var dataStoreLock = new InMemoryDataStoreLock();
        var budgetRepository = new InMemoryBudgetRepository(dataStoreLock);
        var allocationRepository = new InMemoryTransactionAllocationRepository(dataStoreLock);
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        var budget = CreateBudget(budgetItemId);

        budgetRepository.Save(budget);
        var budgetState = budgetRepository.Get(budget.BudgetId);
        Assert.NotNull(budgetState);

        var removed = Assert.IsType<RemoveBudgetItemResult.Removed>(
            budgetState.Value.RemoveBudgetItem(budgetItemId));
        var removeResult = budgetRepository.SaveRemovalIfBudgetItemHasNoAllocations(
            budgetState.Update(removed.Budget),
            budgetItemId,
            allocationRepository.HasAllocationForBudgetItem);

        var allocateResult = allocationRepository.Allocate(
            CreateAllocation(transaction, budgetItemId),
            transactionId => transactionId == transaction.TransactionId,
            requestedBudgetItemId => budgetRepository.GetBudgetItemReference(requestedBudgetItemId) is not null);

        Assert.IsType<BudgetSaveResult.Saved>(removeResult);
        Assert.IsType<AllocateTransactionResult.BudgetItemNotFound>(allocateResult);
        Assert.Null(allocationRepository.Get(transaction.TransactionId));
    }

    [Fact]
    public void Delete_revalidates_budget_item_has_no_allocations_under_the_shared_persistence_lock()
    {
        var dataStoreLock = new InMemoryDataStoreLock();
        var budgetRepository = new InMemoryBudgetRepository(dataStoreLock);
        var allocationRepository = new InMemoryTransactionAllocationRepository(dataStoreLock);
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        var budget = CreateBudget(budgetItemId);

        budgetRepository.Save(budget);
        var budgetState = budgetRepository.Get(budget.BudgetId);
        Assert.NotNull(budgetState);

        var allocateResult = allocationRepository.Allocate(
            CreateAllocation(transaction, budgetItemId),
            transactionId => transactionId == transaction.TransactionId,
            requestedBudgetItemId => budgetRepository.GetBudgetItemReference(requestedBudgetItemId) is not null);

        var removed = Assert.IsType<RemoveBudgetItemResult.Removed>(
            budgetState.Value.RemoveBudgetItem(budgetItemId));
        var removeResult = budgetRepository.SaveRemovalIfBudgetItemHasNoAllocations(
            budgetState.Update(removed.Budget),
            budgetItemId,
            allocationRepository.HasAllocationForBudgetItem);

        Assert.IsType<AllocateTransactionResult.Allocated>(allocateResult);
        Assert.IsType<BudgetSaveResult.BudgetItemHasAllocations>(removeResult);
        Assert.NotNull(budgetRepository.GetBudgetItemReference(budgetItemId));
        Assert.Equal(budgetItemId, allocationRepository.Get(transaction.TransactionId)?.BudgetItemId);
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

    private static TransactionAllocation CreateAllocation(Transaction transaction, Guid budgetItemId)
    {
        return Assert.IsType<AllocateTransactionEntityResult.Allocated>(
            TransactionAllocation.Allocate(transaction, budgetItemId)).Allocation;
    }

    private static Budget CreateBudget(Guid budgetItemId)
    {
        var budget = Assert.IsType<CreateBudgetResult.Created>(
            Budget.Create(Guid.NewGuid(), Name("UK"), Currency("GBP"))).Budget;

        return Assert.IsType<AddBudgetItemResult.Added>(
            budget.AddBudgetItem(
                budgetItemId,
                Name("Groceries"),
                BudgetItemKind.Consumption,
                Money("400.00"))).Budget;
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
