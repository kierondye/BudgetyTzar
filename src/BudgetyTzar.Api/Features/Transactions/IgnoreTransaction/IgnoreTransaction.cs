using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Contracts.Events;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed class IgnoreTransactionHandler(BudgetDbContext db, DomainEventOutboxWriter events)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
        if (transaction is null)
        {
            return CommandResult.NotFound();
        }

        transaction.Ignore();
        var eventId = events.Add(new DomainEvent(
            "TransactionIgnored",
            budgetId,
            nameof(FinancialTransaction),
            transaction.Id,
            $"Ignored transaction {transaction.Description}.",
            Payload: new TransactionIgnoredPayload(
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
