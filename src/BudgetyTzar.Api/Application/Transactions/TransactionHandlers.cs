using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Transactions;

public sealed record TransactionDetail(FinancialTransaction Transaction, IReadOnlyList<TransactionAssignment> Assignments);

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

        var assignedTotal = await db.TransactionAssignments
            .AsNoTracking()
            .Where(x => x.TransactionId == transactionId)
            .SumAsync(x => x.Amount, ct);
        if (amount < assignedTotal)
        {
            return CommandResult.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(amount)] = ["Transaction amount cannot be less than the current assigned total."]
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

public sealed class ReplaceTransactionAssignmentsHandler(BudgetDbContext db, AuditEventWriter audit, BudgetLineEligibilityService eligibility)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid transactionId, IReadOnlyList<TransactionAssignmentItem> assignments, CancellationToken ct)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
        if (transaction is null)
        {
            return CommandResult.NotFound();
        }

        var requestedLineIds = assignments.Select(x => x.BudgetLineId).ToArray();
        if (requestedLineIds.Distinct().Count() != requestedLineIds.Length)
        {
            return CommandResult.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(assignments)] = ["A budget line can only be assigned once per transaction."]
            });
        }

        var budgetLines = await eligibility.GetEligibleBudgetLines(budgetId, requestedLineIds, ct);
        if (budgetLines.Count != requestedLineIds.Length)
        {
            return CommandResult.NotFound();
        }

        var totalAssigned = assignments.Sum(x => x.Amount);
        if (totalAssigned > transaction.Amount)
        {
            return CommandResult.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(assignments)] = ["Total assigned amount cannot exceed the transaction amount."]
            });
        }

        var existing = await db.TransactionAssignments
            .Where(x => x.TransactionId == transactionId)
            .ToListAsync(ct);
        db.TransactionAssignments.RemoveRange(existing);
        db.TransactionAssignments.AddRange(transaction.ReplaceAssignments(assignments));
        audit.Add(new DomainEvent(
            assignments.Count > 1 ? "TransactionSplit" : "TransactionAssigned",
            budgetId,
            nameof(FinancialTransaction),
            transactionId,
            $"Replaced assignments for transaction {transaction.Description}.",
            $"Previous={TransactionAssignmentFormatting.Format(existing)}; New={TransactionAssignmentFormatting.Format(assignments)}",
            Payload: new
            {
                TransactionId = transaction.Id,
                BudgetId = budgetId,
                TransactionAmount = transaction.Amount,
                Assignments = assignments.Select(x => new
                {
                    BudgetItemId = x.BudgetLineId,
                    x.Amount
                }).ToList()
            }));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent();
    }
}

public sealed class ClearTransactionAssignmentsHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
        if (transaction is null)
        {
            return CommandResult.NotFound();
        }

        var assignments = await db.TransactionAssignments
            .Where(x => x.TransactionId == transactionId)
            .ToListAsync(ct);
        db.TransactionAssignments.RemoveRange(assignments);
        audit.Add(new DomainEvent(
            "TransactionAssignmentsCleared",
            budgetId,
            nameof(FinancialTransaction),
            transactionId,
            $"Cleared assignments for transaction {transaction.Description}.",
            TransactionAssignmentFormatting.Format(assignments),
            Payload: new
            {
                TransactionId = transaction.Id,
                BudgetId = budgetId,
                ClearedAssignments = assignments.Select(x => new
                {
                    BudgetItemId = x.BudgetLineId,
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
