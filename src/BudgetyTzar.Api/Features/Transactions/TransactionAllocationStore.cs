namespace BudgetyTzar.Api.Features.Transactions;

public sealed class TransactionAllocationStore
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, TransactionAllocation> allocationsByTransactionId = [];

    public AllocateTransactionResult Allocate(Transaction transaction, Guid budgetItemId)
    {
        lock (syncRoot)
        {
            if (allocationsByTransactionId.TryGetValue(transaction.TransactionId, out var existingAllocation))
            {
                return existingAllocation.BudgetItemId == budgetItemId
                    ? new AllocateTransactionResult.Allocated(existingAllocation)
                    : new AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem();
            }

            var allocation = new TransactionAllocation(
                transaction.TransactionId,
                budgetItemId,
                transaction.Amount,
                transaction.Currency);

            allocationsByTransactionId[transaction.TransactionId] = allocation;

            return new AllocateTransactionResult.Allocated(allocation);
        }
    }

    public TransactionAllocation? Get(Guid transactionId)
    {
        lock (syncRoot)
        {
            return allocationsByTransactionId.GetValueOrDefault(transactionId);
        }
    }

    public void Remove(Guid transactionId)
    {
        lock (syncRoot)
        {
            allocationsByTransactionId.Remove(transactionId);
        }
    }
}

public sealed record TransactionAllocation(
    Guid TransactionId,
    Guid BudgetItemId,
    Common.PositiveMoneyAmount Amount,
    Common.CurrencyCode Currency);

public abstract record AllocateTransactionResult
{
    public sealed record Allocated(TransactionAllocation Allocation) : AllocateTransactionResult;

    public sealed record AlreadyAllocatedToDifferentBudgetItem : AllocateTransactionResult;
}
