using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class PostgreSqlTransactionAllocationRepository : ITransactionAllocationRepository
{
    private readonly BudgetyTzarDbContext context;
    private readonly ApplicationUserId userId;

    public PostgreSqlTransactionAllocationRepository(BudgetyTzarDbContext context, ICurrentUser currentUser)
    {
        this.context = context;
        userId = currentUser.UserId;
    }

    public AllocateTransactionResult Allocate(TransactionAllocation allocation)
    {
        var applicationUserId = GetApplicationUserId();

        if (applicationUserId is null)
        {
            return new AllocateTransactionResult.TransactionNotFound();
        }

        var transaction = context.Transactions
            .AsNoTracking()
            .SingleOrDefault(transaction =>
                transaction.TransactionId == allocation.TransactionId
                && transaction.ApplicationUserId == applicationUserId.Value);

        if (transaction is null)
        {
            return new AllocateTransactionResult.TransactionNotFound();
        }

        var budgetItemExists = context.BudgetItems.Any(budgetItem =>
            budgetItem.BudgetItemId == allocation.BudgetItemId
            && budgetItem.ApplicationUserId == applicationUserId.Value
            && budgetItem.Currency == transaction.Currency);

        if (!budgetItemExists)
        {
            return new AllocateTransactionResult.BudgetItemNotFound();
        }

        var existingAllocation = context.TransactionAllocations
            .AsNoTracking()
            .SingleOrDefault(existing =>
                existing.TransactionId == allocation.TransactionId
                && existing.ApplicationUserId == applicationUserId.Value);

        if (existingAllocation is not null)
        {
            return existingAllocation.BudgetItemId == allocation.BudgetItemId
                ? new AllocateTransactionResult.Allocated(ToAllocation(existingAllocation, transaction))
                : new AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem();
        }

        var allocationRecord = new TransactionAllocationRecord
        {
            TransactionId = allocation.TransactionId,
            ApplicationUserId = applicationUserId.Value,
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
            return GetExistingAllocationResult(allocation, applicationUserId.Value, transaction);
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

    public TransactionAllocation? Get(Guid transactionId)
    {
        var applicationUserId = GetApplicationUserId();

        if (applicationUserId is null)
        {
            return null;
        }

        var allocation = context.TransactionAllocations
            .AsNoTracking()
            .SingleOrDefault(allocation =>
                allocation.TransactionId == transactionId
                && allocation.ApplicationUserId == applicationUserId.Value);

        if (allocation is null)
        {
            return null;
        }

        var transaction = context.Transactions
            .AsNoTracking()
            .Single(transaction =>
                transaction.TransactionId == allocation.TransactionId
                && transaction.ApplicationUserId == applicationUserId.Value);

        return ToAllocation(allocation, transaction);
    }

    public IReadOnlyList<TransactionAllocation> GetAll()
    {
        var applicationUserId = GetApplicationUserId();

        if (applicationUserId is null)
        {
            return [];
        }

        var records = context.TransactionAllocations
            .AsNoTracking()
            .Join(
                context.Transactions.AsNoTracking(),
                allocation => new { allocation.TransactionId, allocation.ApplicationUserId },
                transaction => new { transaction.TransactionId, transaction.ApplicationUserId },
                (allocation, transaction) => new { Allocation = allocation, Transaction = transaction })
            .Where(record => record.Allocation.ApplicationUserId == applicationUserId.Value)
            .OrderBy(record => record.Transaction.RecordedOrder)
            .ToList();

        return records
            .Select(record => ToAllocation(record.Allocation, record.Transaction))
            .ToList();
    }

    public void Remove(Guid transactionId)
    {
        var applicationUserId = GetApplicationUserId();

        if (applicationUserId is null)
        {
            return;
        }

        var allocation = context.TransactionAllocations.SingleOrDefault(allocation =>
            allocation.TransactionId == transactionId
            && allocation.ApplicationUserId == applicationUserId.Value);

        if (allocation is null)
        {
            return;
        }

        context.TransactionAllocations.Remove(allocation);

        try
        {
            context.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
        }
    }

    private Guid? GetApplicationUserId()
    {
        return context.ApplicationUsers
            .AsNoTracking()
            .Where(user => user.UserKey == userId.Value)
            .Select(user => (Guid?)user.ApplicationUserId)
            .SingleOrDefault();
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

        return existingAllocation.BudgetItemId == requestedAllocation.BudgetItemId
            ? new AllocateTransactionResult.Allocated(ToAllocation(existingAllocation, transaction))
            : new AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem();
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
