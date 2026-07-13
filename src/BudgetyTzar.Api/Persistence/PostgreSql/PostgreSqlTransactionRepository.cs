using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class PostgreSqlTransactionRepository : ITransactionRepository
{
    private readonly BudgetyTzarDbContext context;
    private readonly ApplicationUserId userId;

    public PostgreSqlTransactionRepository(BudgetyTzarDbContext context, ICurrentUser currentUser)
    {
        this.context = context;
        userId = currentUser.UserId;
    }

    public void Add(Transaction transaction)
    {
        var applicationUserId = GetOrCreateApplicationUserId();
        var recordedOrder = context.Transactions
            .Where(existing => existing.ApplicationUserId == applicationUserId)
            .Select(existing => (int?)existing.RecordedOrder)
            .Max() + 1 ?? 0;

        context.Transactions.Add(new TransactionRecord
        {
            TransactionId = transaction.TransactionId,
            ApplicationUserId = applicationUserId,
            Description = transaction.Description,
            Type = transaction.Type.Value,
            TransactionDate = transaction.TransactionDate,
            Amount = transaction.Amount.Value,
            Currency = transaction.Currency.Value,
            RecordedOrder = recordedOrder
        });

        context.SaveChanges();
    }

    public IReadOnlyList<Transaction> GetAll()
    {
        var applicationUserId = GetApplicationUserId();

        if (applicationUserId is null)
        {
            return [];
        }

        return context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.ApplicationUserId == applicationUserId.Value)
            .OrderBy(transaction => transaction.RecordedOrder)
            .Select(transaction => ToTransaction(transaction))
            .ToList();
    }

    public Transaction? Get(Guid transactionId)
    {
        var applicationUserId = GetApplicationUserId();

        if (applicationUserId is null)
        {
            return null;
        }

        var transaction = context.Transactions
            .AsNoTracking()
            .SingleOrDefault(transaction =>
                transaction.TransactionId == transactionId
                && transaction.ApplicationUserId == applicationUserId.Value);

        return transaction is null ? null : ToTransaction(transaction);
    }

    public TransactionDeleteResult Delete(Guid transactionId)
    {
        var applicationUserId = GetApplicationUserId();

        if (applicationUserId is null)
        {
            return new TransactionDeleteResult.NotFound();
        }

        var transaction = context.Transactions.SingleOrDefault(transaction =>
            transaction.TransactionId == transactionId
            && transaction.ApplicationUserId == applicationUserId.Value);

        if (transaction is null)
        {
            return new TransactionDeleteResult.NotFound();
        }

        var hasAllocation = context.TransactionAllocations.Any(allocation =>
            allocation.TransactionId == transactionId
            && allocation.ApplicationUserId == applicationUserId.Value);

        if (hasAllocation)
        {
            return new TransactionDeleteResult.TransactionHasAllocation();
        }

        context.Transactions.Remove(transaction);
        context.SaveChanges();

        return new TransactionDeleteResult.Deleted();
    }

    private Guid? GetApplicationUserId()
    {
        return context.ApplicationUsers
            .AsNoTracking()
            .Where(user => user.UserKey == userId.Value)
            .Select(user => (Guid?)user.ApplicationUserId)
            .SingleOrDefault();
    }

    private Guid GetOrCreateApplicationUserId()
    {
        var existingUserId = GetApplicationUserId();

        if (existingUserId is not null)
        {
            return existingUserId.Value;
        }

        var applicationUserId = Guid.NewGuid();
        context.ApplicationUsers.Add(new ApplicationUserRecord
        {
            ApplicationUserId = applicationUserId,
            UserKey = userId.Value
        });
        context.SaveChanges();

        return applicationUserId;
    }

    private static Transaction ToTransaction(TransactionRecord record)
    {
        if (!TransactionType.TryCreate(record.Type, out var type)
            || !PositiveMoneyAmount.TryCreate(record.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), out var amount)
            || !CurrencyCode.TryCreate(record.Currency, out var currency)
            || Transaction.Record(
                record.TransactionId,
                record.Description,
                type,
                record.TransactionDate,
                amount!,
                currency) is not RecordTransactionResult.Recorded recorded)
        {
            throw new InvalidOperationException("Stored transaction data is invalid.");
        }

        return recorded.Transaction;
    }
}
