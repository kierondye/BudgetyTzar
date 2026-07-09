using BudgetyTzar.Api.Domain.Entities;

namespace BudgetyTzar.Api.Features.Transactions;

public interface ITransactionRepository
{
    void Add(Transaction transaction);

    IReadOnlyList<Transaction> GetAll();

    Transaction? Get(Guid transactionId);

    TransactionDeleteResult Delete(Guid transactionId);
}

public abstract record TransactionDeleteResult
{
    public sealed record Deleted : TransactionDeleteResult;

    public sealed record NotFound : TransactionDeleteResult;

    public sealed record TransactionHasAllocation : TransactionDeleteResult;
}

public interface ITransactionAllocationRepository
{
    AllocateTransactionResult Allocate(TransactionAllocation allocation);

    TransactionAllocation? Get(Guid transactionId);

    IReadOnlyList<TransactionAllocation> GetAll();

    void Remove(Guid transactionId);
}

public abstract record AllocateTransactionResult
{
    public sealed record Allocated(TransactionAllocation Allocation) : AllocateTransactionResult;

    public sealed record TransactionNotFound : AllocateTransactionResult;

    public sealed record BudgetItemNotFound : AllocateTransactionResult;

    public sealed record AlreadyAllocatedToDifferentBudgetItem : AllocateTransactionResult;
}
