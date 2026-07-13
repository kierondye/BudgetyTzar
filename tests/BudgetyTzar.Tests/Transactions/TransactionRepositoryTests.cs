using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Tests.Transactions;

public sealed class TransactionRepositoryTests
{
    [Fact]
    public void Get_all_returns_transactions_in_recording_order()
    {
        var repository = CreateRepository();
        var firstTransaction = CreateTransaction("Groceries", "42.50");
        var secondTransaction = CreateTransaction("Fuel", "60.00");

        repository.Add(firstTransaction);
        repository.Add(secondTransaction);

        Assert.Equal(
            [firstTransaction.TransactionId, secondTransaction.TransactionId],
            repository.GetAll().Select(transaction => transaction.TransactionId));
    }

    [Fact]
    public void Get_returns_null_for_missing_transactions()
    {
        var repository = CreateRepository();

        Assert.Null(repository.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Delete_returns_not_found_for_missing_transactions()
    {
        var repository = CreateRepository();

        var result = repository.Delete(Guid.NewGuid());

        Assert.IsType<TransactionDeleteResult.NotFound>(result);
    }

    [Fact]
    public void Delete_removes_unallocated_transactions()
    {
        var repository = CreateRepository();
        var transaction = CreateTransaction("Groceries", "42.50");
        repository.Add(transaction);

        var result = repository.Delete(transaction.TransactionId);

        Assert.IsType<TransactionDeleteResult.Deleted>(result);
        Assert.Null(repository.Get(transaction.TransactionId));
        Assert.Empty(repository.GetAll());
    }

    private static ITransactionRepository CreateRepository()
    {
        return new InMemoryTransactionRepository(new InMemoryDataStore(), CurrentUser("repository-test-user"));
    }

    private static Transaction CreateTransaction(string description, string amount)
    {
        return Assert.IsType<RecordTransactionResult.Recorded>(
            Transaction.Record(
                Guid.NewGuid(),
                description,
                TransactionType.Debit,
                new DateOnly(2026, 7, 2),
                Money(amount),
                Currency("GBP"))).Transaction;
    }

    private static CurrentUser CurrentUser(string value)
    {
        return ExternalIdentity.TryCreate("BudgetyTzar.Tests", value, out var externalIdentity)
            ? new CurrentUser(new InMemoryApplicationUserStore()
                .GetOrCreateApplicationUserId(ApplicationUserKey.FromExternalIdentity(externalIdentity!)))
            : throw new InvalidOperationException("Invalid test user.");
    }

    private static CurrencyCode Currency(string value)
    {
        return CurrencyCode.TryCreate(value, out var currency)
            ? currency
            : throw new InvalidOperationException("Invalid test currency.");
    }

    private static PositiveMoneyAmount Money(string value)
    {
        return PositiveMoneyAmount.TryCreate(value, out var parsedAmount)
            ? parsedAmount!
            : throw new InvalidOperationException("Invalid test amount.");
    }
}
