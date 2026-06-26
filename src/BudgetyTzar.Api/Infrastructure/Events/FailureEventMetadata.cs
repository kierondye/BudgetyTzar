using System.Text.Json;

namespace BudgetyTzar.Api.Infrastructure.Events;

internal readonly record struct FailureEventMetadata(Guid? EventId, string? EventType, Guid? BudgetId);

internal static class FailureEventMetadataReader
{
    public static FailureEventMetadata Read(string eventJson)
    {
        try
        {
            using var document = JsonDocument.Parse(eventJson);
            var root = document.RootElement;
            var eventId = root.TryGetProperty("eventId", out var eventIdElement) && eventIdElement.TryGetGuid(out var parsedEventId)
                ? parsedEventId
                : (Guid?)null;
            var eventType = root.TryGetProperty("eventType", out var eventTypeElement)
                ? eventTypeElement.GetString()
                : null;
            Guid? budgetId = null;
            if (root.TryGetProperty("payload", out var payload)
                && payload.TryGetProperty("budgetId", out var budgetIdElement)
                && budgetIdElement.TryGetGuid(out var parsedBudgetId))
            {
                budgetId = parsedBudgetId;
            }

            return new FailureEventMetadata(eventId, eventType, budgetId);
        }
        catch (JsonException)
        {
            return new FailureEventMetadata(null, null, null);
        }
    }

    public static Guid? TryReadEventId(string eventJson) => Read(eventJson).EventId;

    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
