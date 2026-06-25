using System.Text.Json;
using BudgetyTzar.Api.Contracts.Events;
using BudgetyTzar.Api.Infrastructure.Events;

namespace BudgetyTzar.Api.Application.Reporting;

public sealed class ReportingProjectionDispatcher(ReportingProjectionService projector)
{
    public Task<ProjectionApplyResult> Apply(EventEnvelope envelope, CancellationToken ct) =>
        envelope.EventType switch
        {
            "budgetytzar.budgeting.budget-created.v1" =>
                projector.ApplyBudgetCreated(ReadPayload<BudgetCreatedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
            "budgetytzar.budgeting.budget-item-created.v1" =>
                projector.ApplyBudgetItemCreated(ReadPayload<BudgetItemCreatedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
            "budgetytzar.budgeting.budget-item-archived.v1" =>
                projector.ApplyBudgetItemArchived(ReadPayload<BudgetItemArchivedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
            "budgetytzar.budgeting.budget-adjustment-recorded.v1" =>
                projector.ApplyBudgetAdjustmentRecorded(ReadPayload<BudgetAdjustmentRecordedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
            "budgetytzar.budgeting.budget-reallocation-recorded.v1" =>
                projector.ApplyBudgetReallocationRecorded(ReadPayload<BudgetReallocationRecordedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
            "budgetytzar.transactions.transaction-manually-created.v1" =>
                projector.ApplyTransactionManuallyCreated(ReadPayload<TransactionManuallyCreatedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
            "budgetytzar.transactions.transaction-edited.v1" =>
                projector.ApplyTransactionEdited(ReadPayload<TransactionEditedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
            "budgetytzar.transactions.transaction-ignored.v1" =>
                projector.ApplyTransactionIgnored(ReadPayload<TransactionIgnoredPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
            "budgetytzar.transactions.transaction-allocations-replaced.v1" =>
                projector.ApplyTransactionAllocationsReplaced(ReadPayload<TransactionAllocationsReplacedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
            "budgetytzar.transactions.transaction-allocations-cleared.v1" =>
                projector.ApplyTransactionAllocationsCleared(ReadPayload<TransactionAllocationsClearedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
            _ => throw new PermanentProjectionException($"No reporting projector exists for event type '{envelope.EventType}'.")
        };

    private static TPayload ReadPayload<TPayload>(EventEnvelope envelope)
    {
        try
        {
            return envelope.Payload.Deserialize<TPayload>(EventSerialization.Options)
                ?? throw new PermanentProjectionException($"Event payload for '{envelope.EventType}' could not be deserialized.");
        }
        catch (JsonException ex)
        {
            throw new PermanentProjectionException($"Event payload for '{envelope.EventType}' could not be deserialized.", ex);
        }
    }
}
