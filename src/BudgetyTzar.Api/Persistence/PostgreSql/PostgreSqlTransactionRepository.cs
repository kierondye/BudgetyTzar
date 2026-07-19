using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class PostgreSqlTransactionRepository : ITransactionRepository
{
    private readonly ApplicationDbContext context;
    private readonly ApplicationUserId userId;

    public PostgreSqlTransactionRepository(ApplicationDbContext context, ICurrentUser currentUser)
    {
        this.context = context;
        userId = currentUser.UserId;
        context.UseAuditUser(userId.Value);
    }

    public void Add(Transaction transaction)
    {
        var applicationUserId = userId.Value;

        while (true)
        {
            context.Transactions.Add(CreateRecord(transaction, applicationUserId));

            try
            {
                context.SaveChanges();
                return;
            }
            catch (DbUpdateException exception) when (PostgreSqlPersistenceErrors.IsUniqueViolation(
                exception,
                "ux_transactions_application_user_id_recorded_order"))
            {
                context.ChangeTracker.Clear();
            }
        }
    }

    public IReadOnlyList<Transaction> GetAll()
    {
        var applicationUserId = userId.Value;

        return context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.ApplicationUserId == applicationUserId)
            .OrderBy(transaction => transaction.RecordedOrder)
            .Select(transaction => ToTransaction(transaction))
            .ToList();
    }

    public Transaction? Get(Guid transactionId)
    {
        var applicationUserId = userId.Value;

        var transaction = context.Transactions
            .AsNoTracking()
            .SingleOrDefault(transaction =>
                transaction.TransactionId == transactionId
                && transaction.ApplicationUserId == applicationUserId);

        return transaction is null ? null : ToTransaction(transaction);
    }

    public TransactionDeleteResult Delete(Guid transactionId)
    {
        var applicationUserId = userId.Value;

        var transaction = context.Transactions.SingleOrDefault(transaction =>
            transaction.TransactionId == transactionId
            && transaction.ApplicationUserId == applicationUserId);

        if (transaction is null)
        {
            return new TransactionDeleteResult.NotFound();
        }

        var hasAllocation = context.TransactionAllocations.Any(allocation =>
            allocation.TransactionId == transactionId
            && allocation.ApplicationUserId == applicationUserId);

        if (hasAllocation)
        {
            return new TransactionDeleteResult.TransactionHasAllocation();
        }

        context.Transactions.Remove(transaction);
        try
        {
            context.SaveChanges();
        }
        catch (DbUpdateException exception) when (PostgreSqlPersistenceErrors.IsForeignKeyViolation(
            exception,
            "fk_allocations_transaction_owner_currency"))
        {
            context.ChangeTracker.Clear();
            return new TransactionDeleteResult.TransactionHasAllocation();
        }

        return new TransactionDeleteResult.Deleted();
    }

    private static Transaction ToTransaction(TransactionRecord record)
    {
        if (!TransactionType.TryCreate(record.Type, out var type)
            || !PositiveMoneyAmount.TryCreate(record.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), out var amount)
            || !CurrencyCode.TryCreate(record.Currency, out var currency)
            || Transaction.Create(
                record.TransactionId,
                record.Description,
                type,
                record.TransactionDate,
                amount!,
                currency) is not CreateTransactionResult.Created created)
        {
            throw new InvalidOperationException("Stored transaction data is invalid.");
        }

        return created.Transaction;
    }

    private TransactionRecord CreateRecord(Transaction transaction, Guid applicationUserId)
    {
        var recordedOrder = context.Transactions
            .Where(existing => existing.ApplicationUserId == applicationUserId)
            .Select(existing => (int?)existing.RecordedOrder)
            .Max() + 1 ?? 0;

        return new TransactionRecord
        {
            TransactionId = transaction.TransactionId,
            ApplicationUserId = applicationUserId,
            Description = transaction.Description,
            Type = transaction.Type.Value,
            TransactionDate = transaction.TransactionDate,
            Amount = transaction.Amount.Value,
            Currency = transaction.Currency.Value,
            RecordedOrder = recordedOrder
        };
    }
}
