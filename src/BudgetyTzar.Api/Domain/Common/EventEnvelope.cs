using System.Text.Json.Nodes;

namespace BudgetyTzar.Api;

public sealed record EventEnvelope(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid CorrelationId,
    Guid? CausationId,
    Guid AggregateId,
    string AggregateType,
    int SchemaVersion,
    JsonObject Payload);

public sealed record EventEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid CorrelationId,
    Guid? CausationId,
    Guid AggregateId,
    string AggregateType,
    int SchemaVersion,
    TPayload Payload);

public static class EventTypes
{
    private static readonly IReadOnlyDictionary<string, string> CanonicalNames = new Dictionary<string, string>
    {
        ["BudgetCreated"] = "budgetytzar.budgeting.budget-created.v1",
        ["BudgetItemCreated"] = "budgetytzar.budgeting.budget-item-created.v1",
        ["BudgetItemArchived"] = "budgetytzar.budgeting.budget-item-archived.v1",
        ["BudgetReallocationRecorded"] = "budgetytzar.budgeting.budget-reallocation-recorded.v1",
        ["BudgetAdjustmentRecorded"] = "budgetytzar.budgeting.budget-adjustment-recorded.v1",
        ["TransactionManuallyCreated"] = "budgetytzar.transactions.transaction-manually-created.v1",
        ["TransactionEdited"] = "budgetytzar.transactions.transaction-edited.v1",
        ["TransactionIgnored"] = "budgetytzar.transactions.transaction-ignored.v1",
        ["TransactionAllocationRecorded"] = "budgetytzar.transactions.transaction-allocation-recorded.v1",
        ["TransactionAllocationsReplaced"] = "budgetytzar.transactions.transaction-allocations-replaced.v1",
        ["TransactionAllocationsCleared"] = "budgetytzar.transactions.transaction-allocations-cleared.v1"
    };

    public static string ToCanonical(string eventType) =>
        CanonicalNames.TryGetValue(eventType, out var canonicalName)
            ? canonicalName
            : $"budgetytzar.internal.{ToKebabCase(eventType)}.v1";

    public static string ToTopic(string canonicalEventType, EventTopicOptions topics)
    {
        if (canonicalEventType.StartsWith("budgetytzar.transactions.", StringComparison.Ordinal))
        {
            return topics.Transactions;
        }

        if (canonicalEventType.StartsWith("budgetytzar.reporting.", StringComparison.Ordinal))
        {
            return topics.Reporting;
        }

        return topics.Budgeting;
    }

    private static string ToKebabCase(string value)
    {
        var chars = new List<char>(value.Length * 2);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current) && i > 0)
            {
                chars.Add('-');
            }

            chars.Add(char.ToLowerInvariant(current));
        }

        return new string(chars.ToArray());
    }
}

public sealed class EventTopicOptions
{
    public string Budgeting { get; set; } = "budgetytzar.budgeting.events";
    public string Transactions { get; set; } = "budgetytzar.transactions.events";
    public string Reporting { get; set; } = "budgetytzar.reporting.events";
}
