using System.Text.Json;
using System.Text.Json.Nodes;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Reporting;

public sealed record AuditProjectionResult(Guid BudgetId);

public sealed class AuditEventProjectionService(BudgetDbContext db)
{
    private static readonly IReadOnlyDictionary<string, string> LegacyEventTypes = new Dictionary<string, string>
    {
        ["budgetytzar.budgeting.budget-created.v1"] = "BudgetCreated",
        ["budgetytzar.budgeting.budget-item-created.v1"] = "BudgetItemCreated",
        ["budgetytzar.budgeting.budget-item-archived.v1"] = "BudgetItemArchived",
        ["budgetytzar.budgeting.budget-adjustment-recorded.v1"] = "BudgetAdjustmentRecorded",
        ["budgetytzar.budgeting.budget-reallocation-recorded.v1"] = "BudgetReallocationRecorded",
        ["budgetytzar.transactions.transaction-manually-created.v1"] = "TransactionManuallyCreated",
        ["budgetytzar.transactions.transaction-edited.v1"] = "TransactionEdited",
        ["budgetytzar.transactions.transaction-ignored.v1"] = "TransactionIgnored",
        ["budgetytzar.transactions.transaction-allocations-replaced.v1"] = "TransactionAllocationsReplaced",
        ["budgetytzar.transactions.transaction-allocations-cleared.v1"] = "TransactionAllocationsCleared"
    };

    public async Task<AuditProjectionResult> Apply(EventEnvelope envelope, CancellationToken ct)
    {
        if (await db.AuditEvents.AnyAsync(x => x.Id == envelope.EventId, ct))
        {
            return new AuditProjectionResult(ReadRequiredGuid(envelope.Payload, "budgetId"));
        }

        var budgetId = ReadRequiredGuid(envelope.Payload, "budgetId");
        var legacyEventType = ToLegacyEventType(envelope.EventType);
        var description = DescriptionFor(envelope.EventType, envelope.Payload);
        var details = DetailsFor(envelope.Payload);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = envelope.EventId,
            OccurredAt = envelope.OccurredAt,
            BudgetId = budgetId,
            AppliesToAllPeriods = envelope.EventType == "budgetytzar.budgeting.budget-created.v1",
            EntityType = envelope.AggregateType,
            EntityId = envelope.AggregateId,
            EventType = legacyEventType,
            Description = description,
            Details = details
        });
        await db.SaveChangesAsync(ct);
        return new AuditProjectionResult(budgetId);
    }

    private static string ToLegacyEventType(string canonicalEventType) =>
        LegacyEventTypes.TryGetValue(canonicalEventType, out var legacyEventType)
            ? legacyEventType
            : canonicalEventType;

    private static string DescriptionFor(string eventType, JsonObject payload) =>
        eventType switch
        {
            "budgetytzar.budgeting.budget-created.v1" =>
                $"Created budget {ReadString(payload, "name") ?? ReadRequiredGuid(payload, "budgetId").ToString()}.",
            "budgetytzar.budgeting.budget-item-created.v1" =>
                $"Created budget item {ReadString(payload, "name") ?? ReadRequiredGuid(payload, "budgetItemId").ToString()}.",
            "budgetytzar.budgeting.budget-item-archived.v1" =>
                $"Archived budget item {ReadString(payload, "name") ?? ReadRequiredGuid(payload, "budgetItemId").ToString()}.",
            "budgetytzar.budgeting.budget-adjustment-recorded.v1" =>
                $"Recorded {ReadString(payload, "direction") ?? "budget"} adjustment {ReadDecimal(payload, "amount")?.ToString("0.##") ?? string.Empty}.",
            "budgetytzar.budgeting.budget-reallocation-recorded.v1" =>
                "Recorded budget reallocation.",
            "budgetytzar.transactions.transaction-manually-created.v1" =>
                $"Created transaction {ReadString(payload, "description") ?? ReadRequiredGuid(payload, "transactionId").ToString()}.",
            "budgetytzar.transactions.transaction-edited.v1" =>
                $"Edited transaction {ReadString(payload, "description") ?? ReadRequiredGuid(payload, "transactionId").ToString()}.",
            "budgetytzar.transactions.transaction-ignored.v1" =>
                $"Ignored transaction {ReadString(payload, "description") ?? ReadRequiredGuid(payload, "transactionId").ToString()}.",
            "budgetytzar.transactions.transaction-allocations-replaced.v1" =>
                "Replaced transaction allocations.",
            "budgetytzar.transactions.transaction-allocations-cleared.v1" =>
                "Cleared transaction allocations.",
            _ => $"Processed event {eventType}."
        };

    private static string DetailsFor(JsonObject payload) =>
        Truncate(JsonSerializer.Serialize(payload, EventSerialization.Options), 4000);

    private static Guid ReadRequiredGuid(JsonObject payload, string propertyName)
    {
        if (payload[propertyName] is { } node
            && node.GetValueKind() == JsonValueKind.String
            && Guid.TryParse(node.GetValue<string>(), out var guid))
        {
            return guid;
        }

        if (payload[propertyName] is { } guidNode
            && guidNode.Deserialize<Guid?>(EventSerialization.Options) is { } parsedGuid)
        {
            return parsedGuid;
        }

        throw new PermanentProjectionException($"Event payload is missing required GUID property '{propertyName}'.");
    }

    private static string? ReadString(JsonObject payload, string propertyName) =>
        payload[propertyName] is { } node && node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : null;

    private static decimal? ReadDecimal(JsonObject payload, string propertyName) =>
        payload[propertyName]?.Deserialize<decimal?>(EventSerialization.Options);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
