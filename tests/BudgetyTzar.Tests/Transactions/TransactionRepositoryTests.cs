using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Tests.Transactions;

public sealed class TransactionRepositoryContractTests
{
    [Fact]
    public void Delete_reports_not_found_without_changing_storage()
    {
        ITransactionRepository repository = new InMemoryTransactionRepository();

        var result = repository.Delete(Guid.NewGuid());

        Assert.IsType<TransactionDeleteResult.NotFound>(result);
        Assert.Empty(repository.GetAll());
    }

    [Fact]
    public void Delete_removes_an_existing_transaction()
    {
        ITransactionRepository repository = new InMemoryTransactionRepository();
        var transaction = CreateTransaction();
        repository.Add(transaction);

        var result = repository.Delete(transaction.TransactionId);

        Assert.IsType<TransactionDeleteResult.Deleted>(result);
        Assert.Null(repository.Get(transaction.TransactionId));
        Assert.Empty(repository.GetAll());
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
