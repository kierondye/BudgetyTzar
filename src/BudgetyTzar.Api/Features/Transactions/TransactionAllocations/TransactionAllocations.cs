using BudgetyTzar.Api.Application.Budgeting;
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
        var validationError = transaction.ValidateReplacementAllocations(allocations);
        if (validationError is not null)
        {
            return CommandResult.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(allocations)] = [validationError]
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

        var existing = await db.TransactionAllocations
            .Where(x => x.TransactionId == transactionId)
            .ToListAsync(ct);
        db.TransactionAllocations.RemoveRange(existing);
        db.TransactionAllocations.AddRange(transaction.ReplaceAllocations(allocations));
        var eventId = events.Add(transaction.AllocationsReplacedEvent(existing, allocations));
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
        var eventId = events.Add(transaction.AllocationsClearedEvent(allocations));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent(eventId);
    }
}
