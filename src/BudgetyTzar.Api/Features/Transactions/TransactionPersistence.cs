using BudgetyTzar.Api.Domain.Entities;

namespace BudgetyTzar.Api.Features.Transactions;

public interface ITransactionRepository
{
    void Add(Transaction transaction);

    IReadOnlyList<Transaction> GetAll();

    Transaction? Get(Guid transactionId);

    TransactionDeleteResult Delete(Guid transactionId);
}

public interface ITransactionAllocationRepository
{
    AllocateTransactionResult Allocate(TransactionAllocation allocation);

    TransactionAllocation? Get(Guid transactionId);

    IReadOnlyList<TransactionAllocation> GetAll();

    void Remove(Guid transactionId);
}
