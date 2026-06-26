namespace BudgetyTzar.Api.Application.Reporting;

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
