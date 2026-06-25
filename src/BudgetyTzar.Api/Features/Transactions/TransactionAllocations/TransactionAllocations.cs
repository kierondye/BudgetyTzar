using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Contracts.Events;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record ReplaceTransactionAllocationsRequest(IReadOnlyList<TransactionAllocationItem> Allocations);

public sealed class ReplaceTransactionAllocationsValidator : AbstractValidator<ReplaceTransactionAllocationsRequest>
{
    public ReplaceTransactionAllocationsValidator()
    {
        RuleFor(x => x.Allocations).NotNull();
        RuleForEach(x => x.Allocations).ChildRules(item =>
        {
            item.RuleFor(x => x.BudgetItemId).NotEmpty();
            item.RuleFor(x => x.Amount).PositiveAmount();
            item.RuleFor(x => x.Notes).MaximumLength(500);
        });
    }
}

public sealed class ReplaceTransactionAllocationsHandler(
    BudgetDbContext db,
    DomainEventOutboxWriter events,
    BudgetItemEligibilityService eligibility)
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
        var eventId = events.Add(new DomainEvent(
            "TransactionAllocationsReplaced",
            budgetId,
            nameof(FinancialTransaction),
            transactionId,
            $"Allocated transaction {transaction.Description}.",
            $"Previous={TransactionAllocationFormatting.Format(existing)}; New={TransactionAllocationFormatting.Format(allocations)}",
            Payload: new TransactionAllocationsReplacedPayload(
                transaction.Id,
                budgetId,
                transaction.Amount,
                allocations
                    .Select(x => new TransactionAllocationPayload(
                        x.BudgetItemId,
                        x.Amount,
                        string.IsNullOrWhiteSpace(x.Notes) ? null : x.Notes.Trim()))
                    .ToList())));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent(eventId);
    }
}

public sealed class ClearTransactionAllocationsHandler(BudgetDbContext db, DomainEventOutboxWriter events)
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
        var eventId = events.Add(new DomainEvent(
            "TransactionAllocationsCleared",
            budgetId,
            nameof(FinancialTransaction),
            transactionId,
            $"Cleared allocations for transaction {transaction.Description}.",
            TransactionAllocationFormatting.Format(allocations),
            Payload: new TransactionAllocationsClearedPayload(
                transaction.Id,
                budgetId,
                allocations
                    .Select(x => new TransactionAllocationPayload(x.BudgetItemId, x.Amount, x.Notes))
                    .ToList())));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent(eventId);
    }
}

internal static class TransactionAllocationFormatting
{
    public static string Format(IEnumerable<TransactionAllocation> allocations) =>
        string.Join("; ", allocations.Select(x => Format(x.BudgetItemId, x.Amount, x.Notes)));

    public static string Format(IEnumerable<TransactionAllocationItem> allocations) =>
        string.Join("; ", allocations.Select(x => Format(x.BudgetItemId, x.Amount, x.Notes)));

    private static string Format(Guid budgetItemId, decimal amount, string? notes) =>
        string.IsNullOrWhiteSpace(notes)
            ? $"{budgetItemId}:{amount}"
            : $"{budgetItemId}:{amount} ({notes.Trim()})";
}
