using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Contracts.Events;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record UpdateTransactionRequest(
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    TransactionDirection Direction,
    string? SourceAccount,
    string? ExternalReference,
    string? Notes);

public sealed class UpdateTransactionValidator : AbstractValidator<UpdateTransactionRequest>
{
    public UpdateTransactionValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(240);
        RuleFor(x => x.Amount).PositiveAmount();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.SourceAccount).MaximumLength(120);
        RuleFor(x => x.ExternalReference).MaximumLength(160);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class UpdateTransactionHandler(BudgetDbContext db, DomainEventOutboxWriter events)
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

        var eventId = events.Add(new DomainEvent(
            "TransactionEdited",
            budgetId,
            nameof(FinancialTransaction),
            transaction.Id,
            $"Edited transaction {transaction.Description}.",
            $"Previous={previousDescription}, {previousAmount} {previousDirection}; New={transaction.Description}, {transaction.Amount} {transaction.Direction}",
            Payload: new TransactionEditedPayload(
                transaction.Id,
                transaction.BudgetId,
                transaction.TransactionDate,
                transaction.Description,
                transaction.Amount,
                transaction.Direction,
                transaction.SourceAccount,
                transaction.ExternalReference,
                transaction.Notes,
                transaction.IsIgnored)));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent(eventId);
    }
}
