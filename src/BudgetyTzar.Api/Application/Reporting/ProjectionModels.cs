namespace BudgetyTzar.Api.Application.Reporting;

public enum ProjectionProcessingStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

public sealed class ProcessedProjectionEvent
{
    public Guid EventId { get; set; }
    public required string EventType { get; set; }
    public Guid? BudgetId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
    public ProjectionProcessingStatus Status { get; set; } = ProjectionProcessingStatus.Pending;
    public Guid? ProcessingInstanceId { get; set; }
    public DateTimeOffset? ProcessingStartedAt { get; set; }
    public DateTimeOffset? ProcessingUpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastError { get; set; }
}

public enum ProjectionFailureCategory
{
    Validation,
    Projection,
    DeadLetterPublish
}

public enum ProjectionFailureStatus
{
    Pending,
    DeadLettered,
    Retryable
}

public sealed class ProjectionEventFailure
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? EventId { get; set; }
    public required string Topic { get; set; }
    public int Partition { get; set; }
    public long Offset { get; set; }
    public required string ConsumerGroup { get; set; }
    public string? EventType { get; set; }
    public Guid? BudgetId { get; set; }
    public ProjectionFailureCategory Category { get; set; }
    public ProjectionFailureStatus Status { get; set; }
    public int RetryCount { get; set; }
    public required string LastError { get; set; }
    public required string RawEventJson { get; set; }
    public DateTimeOffset FirstFailedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastFailedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum AuditFailureCategory
{
    Validation,
    AuditProjection,
    DeadLetterPublish
}

public enum AuditFailureStatus
{
    Pending,
    DeadLettered,
    Retryable
}

public sealed class AuditEventFailure
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? EventId { get; set; }
    public required string Topic { get; set; }
    public int Partition { get; set; }
    public long Offset { get; set; }
    public required string ConsumerGroup { get; set; }
    public string? EventType { get; set; }
    public Guid? BudgetId { get; set; }
    public AuditFailureCategory Category { get; set; }
    public AuditFailureStatus Status { get; set; }
    public int RetryCount { get; set; }
    public required string LastError { get; set; }
    public required string RawEventJson { get; set; }
    public DateTimeOffset FirstFailedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastFailedAt { get; set; } = DateTimeOffset.UtcNow;
}
