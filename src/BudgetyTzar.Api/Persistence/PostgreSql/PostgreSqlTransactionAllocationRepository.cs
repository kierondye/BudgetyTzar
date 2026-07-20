using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Audit;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class PostgreSqlTransactionAllocationRepository : ITransactionAllocationRepository
{
    private readonly ApplicationDbContext context;
    private readonly ApplicationUserId userId;
    private readonly IAuditRequestContext auditContext;

    public PostgreSqlTransactionAllocationRepository(
        ApplicationDbContext context,
        ICurrentUser currentUser,
        IAuditRequestContext? auditContext = null)
    {
        this.context = context;
        userId = currentUser.UserId;
        this.auditContext = auditContext ?? new RepositoryAuditRequestContext();
    }

    public AllocateTransactionResult Allocate(TransactionAllocation allocation)
    {
        var applicationUserId = userId.Value;

        while (true)
        {
            var transaction = context.Transactions
                .AsNoTracking()
                .SingleOrDefault(transaction =>
                    transaction.TransactionId == allocation.TransactionId
                    && transaction.ApplicationUserId == applicationUserId);

            if (transaction is null)
            {
                return new AllocateTransactionResult.TransactionNotFound();
            }

            var budgetItemExists = context.BudgetItems.Any(budgetItem =>
                budgetItem.BudgetItemId == allocation.BudgetItemId
                && budgetItem.ApplicationUserId == applicationUserId
                && budgetItem.Currency == transaction.Currency);

            if (!budgetItemExists)
            {
                return new AllocateTransactionResult.BudgetItemNotFound();
            }

            var existingAllocation = context.TransactionAllocations
                .AsNoTracking()
                .SingleOrDefault(existing =>
                    existing.TransactionId == allocation.TransactionId
                    && existing.ApplicationUserId == applicationUserId);

            if (existingAllocation is not null)
            {
                if (existingAllocation.BudgetItemId != allocation.BudgetItemId)
                {
                    return new AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem();
                }

                return new AllocateTransactionResult.Allocated(ToAllocation(existingAllocation, transaction));
            }

            var allocationRecord = new TransactionAllocationRecord
            {
                TransactionId = allocation.TransactionId,
                ApplicationUserId = applicationUserId,
                BudgetItemId = allocation.BudgetItemId,
                Currency = transaction.Currency
            };

            context.TransactionAllocations.Add(allocationRecord);
            context.AddAuditRecords(allocation.AuditFacts, applicationUserId, auditContext);

            try
            {
                context.SaveChanges();
            }
            catch (DbUpdateException exception) when (IsAllocationAlreadyStored(exception))
            {
                context.ChangeTracker.Clear();
                continue;
            }
            catch (DbUpdateException exception) when (PostgreSqlPersistenceErrors.IsForeignKeyViolation(
                exception,
                "fk_allocations_transaction_owner_currency"))
            {
                context.ChangeTracker.Clear();
                return new AllocateTransactionResult.TransactionNotFound();
            }
            catch (DbUpdateException exception) when (PostgreSqlPersistenceErrors.IsForeignKeyViolation(
                exception,
                "fk_allocations_budget_item_owner_currency"))
            {
                context.ChangeTracker.Clear();
                return new AllocateTransactionResult.BudgetItemNotFound();
            }

            return new AllocateTransactionResult.Allocated(allocation);
        }
    }

    public TransactionAllocation? Get(Guid transactionId)
    {
        var applicationUserId = userId.Value;

        var allocation = context.TransactionAllocations
            .AsNoTracking()
            .SingleOrDefault(allocation =>
                allocation.TransactionId == transactionId
                && allocation.ApplicationUserId == applicationUserId);

        if (allocation is null)
        {
            return null;
        }

        var transaction = context.Transactions
            .AsNoTracking()
            .Single(transaction =>
                transaction.TransactionId == allocation.TransactionId
                && transaction.ApplicationUserId == applicationUserId);

        return ToAllocation(allocation, transaction);
    }

    public IReadOnlyList<TransactionAllocation> GetAll()
    {
        var applicationUserId = userId.Value;

        var records = context.TransactionAllocations
            .AsNoTracking()
            .Join(
                context.Transactions.AsNoTracking(),
                allocation => new { allocation.TransactionId, allocation.ApplicationUserId },
                transaction => new { transaction.TransactionId, transaction.ApplicationUserId },
                (allocation, transaction) => new { Allocation = allocation, Transaction = transaction })
            .Where(record => record.Allocation.ApplicationUserId == applicationUserId)
            .OrderBy(record => record.Transaction.RecordedOrder)
            .ToList();

        return records
            .Select(record => ToAllocation(record.Allocation, record.Transaction))
            .ToList();
    }

    public RemoveTransactionAllocationResult Remove(Guid transactionId)
    {
        var applicationUserId = userId.Value;

        var record = context.TransactionAllocations.SingleOrDefault(allocation =>
            allocation.TransactionId == transactionId
            && allocation.ApplicationUserId == applicationUserId);

        if (record is null)
        {
            return new RemoveTransactionAllocationResult.NotFound();
        }

        var transaction = context.Transactions
            .AsNoTracking()
            .Single(transaction =>
                transaction.TransactionId == record.TransactionId
                && transaction.ApplicationUserId == applicationUserId);
        var allocation = ToAllocation(record, transaction);
        if (allocation.Remove() is not RemoveTransactionAllocationEntityResult.Removed removed)
        {
            throw new InvalidOperationException("Unexpected remove allocation result.");
        }

        context.TransactionAllocations.Remove(record);
        context.AddAuditRecords(removed.Allocation.AuditFacts, applicationUserId, auditContext);

        try
        {
            context.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return new RemoveTransactionAllocationResult.NotFound();
        }

        return new RemoveTransactionAllocationResult.Removed(removed.Allocation);
    }

    private static bool IsAllocationAlreadyStored(DbUpdateException exception)
    {
        return PostgreSqlPersistenceErrors.IsUniqueViolation(exception, "pk_transaction_allocations")
            || PostgreSqlPersistenceErrors.IsUniqueViolation(exception, "ux_allocations_transaction_owner_currency");
    }

    private static TransactionAllocation ToAllocation(
        TransactionAllocationRecord allocation,
        TransactionRecord transaction)
    {
        var domainTransaction = ToTransaction(transaction);

        return new TransactionAllocation(
            allocation.TransactionId,
            allocation.BudgetItemId,
            domainTransaction.Amount,
            domainTransaction.Currency);
    }

    private static Transaction ToTransaction(TransactionRecord record)
    {
        if (!TransactionType.TryCreate(record.Type, out var type)
            || !PositiveMoneyAmount.TryCreate(record.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), out var amount)
            || !CurrencyCode.TryCreate(record.Currency, out var currency)
            || string.IsNullOrWhiteSpace(record.Description))
        {
            throw new InvalidOperationException("Stored transaction data is invalid.");
        }

        return new Transaction(
            record.TransactionId,
            record.Description,
            type,
            record.TransactionDate,
            amount!,
            currency);
    }
}
