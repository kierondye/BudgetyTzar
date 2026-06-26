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
