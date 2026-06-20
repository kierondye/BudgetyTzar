using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Transactions;

public sealed record TransactionDetail(FinancialTransaction Transaction, IReadOnlyList<TransactionAllocation> Allocations);

public sealed class CreateTransactionHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult<FinancialTransaction>> Handle(
        Guid budgetId,
        DateOnly transactionDate,
        string description,
        decimal amount,
        TransactionDirection direction,
        string? sourceAccount,
        string? externalReference,
        string? notes,
        CancellationToken ct)
    {
        if (!await db.Budgets.AnyAsync(x => x.Id == budgetId, ct))
        {
            return CommandResult<FinancialTransaction>.NotFound();
        }

        var transaction = FinancialTransaction.Create(budgetId, transactionDate, description, amount, direction, sourceAccount, externalReference, notes);
        db.Transactions.Add(transaction);
        audit.Add(new DomainEvent(
            "TransactionManuallyCreated",
            budgetId,
            nameof(FinancialTransaction),
            transaction.Id,
            $"Created transaction {transaction.Description} for {transaction.Amount} {transaction.Direction}.",
            Payload: TransactionEventPayloads.TransactionPayload(transaction)));
        await db.SaveChangesAsync(ct);
        return CommandResult<FinancialTransaction>.Created(transaction);
    }
}

public sealed class UpdateTransactionHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult> Handle(
        Guid budgetId,
        Guid transactionId,
        DateOnly transactionDate,
        string description,
        decimal amount,
        TransactionDirection direction,
        string? sourceAccount,
        string? externalReference,
        string? notes,
        CancellationToken ct)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
        if (transaction is null)
        {
            return CommandResult.NotFound();
        }

        var allocatedTotal = await db.TransactionAllocations
            .AsNoTracking()
            .Where(x => x.TransactionId == transactionId)
            .SumAsync(x => x.Amount, ct);
        if (amount < allocatedTotal)
        {
            return CommandResult.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(amount)] = ["Transaction amount cannot be less than the current allocated total."]
            });
        }

        var previousDescription = transaction.Description;
        var previousAmount = transaction.Amount;
        var previousDirection = transaction.Direction;

        transaction.Edit(transactionDate, description, amount, direction, sourceAccount, externalReference, notes);

        audit.Add(new DomainEvent(
            "TransactionEdited",
            budgetId,
            nameof(FinancialTransaction),
            transaction.Id,
            $"Edited transaction {transaction.Description}.",
            $"Previous={previousDescription}, {previousAmount} {previousDirection}; New={transaction.Description}, {transaction.Amount} {transaction.Direction}",
            Payload: TransactionEventPayloads.TransactionPayload(transaction)));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent();
    }
}

public sealed class IgnoreTransactionHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
        if (transaction is null)
        {
            return CommandResult.NotFound();
        }

        transaction.Ignore();
        audit.Add(new DomainEvent(
            "TransactionIgnored",
            budgetId,
            nameof(FinancialTransaction),
            transaction.Id,
            $"Ignored transaction {transaction.Description}.",
            Payload: TransactionEventPayloads.TransactionPayload(transaction)));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent();
    }
}

public sealed class ReplaceTransactionAllocationsHandler(BudgetDbContext db, AuditEventWriter audit, BudgetItemEligibilityService eligibility)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid transactionId, IReadOnlyList<TransactionAllocationItem> allocations, CancellationToken ct)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
        if (transaction is null)
        {
            return CommandResult.NotFound();
        }

        var requestedItemIds = allocations.Select(x => x.BudgetItemId).ToArray();
        if (requestedItemIds.Distinct().Count() != requestedItemIds.Length)
        {
            return CommandResult.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(allocations)] = ["A budget item can only be allocated once per transaction."]
            });
        }

        var budgetItems = await eligibility.GetBudgetItems(budgetId, requestedItemIds, ct);
        if (budgetItems.Count != requestedItemIds.Length)
        {
            return CommandResult.NotFound();
        }

        if (budgetItems.Any(x => !x.CanAcceptActivityOn(transaction.TransactionDate)))
        {
            return CommandResult.ValidationProblem(BudgetItemValidationErrors.ArchivedBudgetItemErrors());
        }

        var totalAllocated = allocations.Sum(x => x.Amount);
        if (totalAllocated > transaction.Amount)
        {
            return CommandResult.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(allocations)] = ["Total allocated amount cannot exceed the transaction amount."]
            });
        }

        var existing = await db.TransactionAllocations
            .Where(x => x.TransactionId == transactionId)
            .ToListAsync(ct);
        db.TransactionAllocations.RemoveRange(existing);
        db.TransactionAllocations.AddRange(transaction.ReplaceAllocations(allocations));
        audit.Add(new DomainEvent(
            "TransactionAllocationsReplaced",
            budgetId,
            nameof(FinancialTransaction),
            transactionId,
            $"Allocated transaction {transaction.Description}.",
            $"Previous={TransactionAllocationFormatting.Format(existing)}; New={TransactionAllocationFormatting.Format(allocations)}",
            Payload: new
            {
                TransactionId = transaction.Id,
                BudgetId = budgetId,
                TransactionAmount = transaction.Amount,
                Allocations = allocations.Select(x => new
                {
                    BudgetItemId = x.BudgetItemId,
                    x.Amount
                }).ToList()
            }));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent();
    }
}

public sealed class ClearTransactionAllocationsHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
        if (transaction is null)
        {
            return CommandResult.NotFound();
        }

        var allocations = await db.TransactionAllocations
            .Where(x => x.TransactionId == transactionId)
            .ToListAsync(ct);
        db.TransactionAllocations.RemoveRange(allocations);
        audit.Add(new DomainEvent(
            "TransactionAllocationsCleared",
            budgetId,
            nameof(FinancialTransaction),
            transactionId,
            $"Cleared allocations for transaction {transaction.Description}.",
            TransactionAllocationFormatting.Format(allocations),
            Payload: new
            {
                TransactionId = transaction.Id,
                BudgetId = budgetId,
                ClearedAllocations = allocations.Select(x => new
                {
                    BudgetItemId = x.BudgetItemId,
                    x.Amount
                }).ToList()
            }));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent();
    }
}

file static class TransactionEventPayloads
{
    public static object TransactionPayload(FinancialTransaction transaction) => new
    {
        TransactionId = transaction.Id,
        transaction.BudgetId,
        transaction.TransactionDate,
        transaction.Description,
        transaction.Amount,
        Direction = transaction.Direction,
        transaction.SourceAccount,
        transaction.ExternalReference,
        transaction.Notes,
        transaction.IsIgnored
    };
}
