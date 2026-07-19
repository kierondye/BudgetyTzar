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

    public PostgreSqlTransactionAllocationRepository(ApplicationDbContext context, ICurrentUser currentUser)
    {
        this.context = context;
        userId = currentUser.UserId;
        context.UseAuditUser(userId.Value);
    }

    public AllocateTransactionResult Allocate(TransactionAllocation allocation)
    {
        var applicationUserId = userId.Value;

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

            var existing = ToAllocation(existingAllocation, transaction);
            RecordIdempotentAllocation(existing);
            return new AllocateTransactionResult.Allocated(existing, WasCreated: false);
        }

        var allocationRecord = new TransactionAllocationRecord
        {
            TransactionId = allocation.TransactionId,
            ApplicationUserId = applicationUserId,
            BudgetItemId = allocation.BudgetItemId,
            Currency = transaction.Currency
        };

        context.TransactionAllocations.Add(allocationRecord);

        try
        {
            context.SaveChanges();
        }
        catch (DbUpdateException exception) when (IsAllocationAlreadyStored(exception))
        {
            context.ChangeTracker.Clear();
            return GetExistingAllocationResult(allocation, applicationUserId, transaction);
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

        return new AllocateTransactionResult.Allocated(allocation, WasCreated: true);
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

        context.TransactionAllocations.Remove(record);

        try
        {
            context.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return new RemoveTransactionAllocationResult.NotFound();
        }

        return new RemoveTransactionAllocationResult.Removed(allocation);
    }

    private AllocateTransactionResult GetExistingAllocationResult(
        TransactionAllocation requestedAllocation,
        Guid applicationUserId,
        TransactionRecord transaction)
    {
        var existingAllocation = context.TransactionAllocations
            .AsNoTracking()
            .SingleOrDefault(existing =>
                existing.TransactionId == requestedAllocation.TransactionId
                && existing.ApplicationUserId == applicationUserId);

        if (existingAllocation is null)
        {
            throw new InvalidOperationException("Allocation conflict was not available after a concurrent create.");
        }

        if (existingAllocation.BudgetItemId != requestedAllocation.BudgetItemId)
        {
            return new AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem();
        }

        var existing = ToAllocation(existingAllocation, transaction);
        RecordIdempotentAllocation(existing);
        return new AllocateTransactionResult.Allocated(existing, WasCreated: false);
    }

    private void RecordIdempotentAllocation(TransactionAllocation allocation)
    {
        context.RecordAudit(AuditEntry.TransactionAllocationIdempotent(allocation));
        context.SaveChanges();
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

        if (TransactionAllocation.Allocate(domainTransaction, allocation.BudgetItemId)
            is not AllocateTransactionEntityResult.Allocated allocated)
        {
            throw new InvalidOperationException("Stored allocation data is invalid.");
        }

        return allocated.Allocation;
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
}
