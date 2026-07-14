namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class AuditRecord
{
    public Guid AuditRecordId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public Guid ApplicationUserId { get; set; }

    public Guid? ActorApplicationUserId { get; set; }

    public string OperationName { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;

    public Guid ResourceId { get; set; }

    public string? BeforeState { get; set; }

    public string? AfterState { get; set; }
}
