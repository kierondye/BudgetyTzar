using BudgetyTzar.Api.Domain.Entities;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class AuditRecord
{
    public Guid AuditRecordId { get; set; }

    public DateTimeOffset PersistedAtUtc { get; set; }

    public Guid ApplicationUserId { get; set; }

    public string OperationName { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public static AuditRecord From(AuditFact fact, AuditRecordContext context)
    {
        return new AuditRecord
        {
            AuditRecordId = fact.Id,
            PersistedAtUtc = context.PersistedAtUtc,
            ApplicationUserId = context.ApplicationUserId,
            OperationName = context.OperationName,
            CorrelationId = context.CorrelationId,
            Action = fact.Action.ToString(),
            OldValue = fact.OldValue,
            NewValue = fact.NewValue
        };
    }
}

public sealed record AuditRecordContext(
    Guid ApplicationUserId,
    string OperationName,
    string CorrelationId,
    DateTimeOffset PersistedAtUtc);
