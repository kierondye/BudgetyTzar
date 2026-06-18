namespace BudgetyTzar.Api;

public enum OutboxMessageStatus
{
    Pending,
    Published,
    Failed
}

public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Topic { get; set; }
    public required string EventType { get; set; }
    public Guid AggregateId { get; set; }
    public required string AggregateType { get; set; }
    public Guid? BudgetId { get; set; }
    public required string EnvelopeJson { get; set; }
    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ProjectedAt { get; set; }

    public void MarkPublished(DateTimeOffset now)
    {
        Status = OutboxMessageStatus.Published;
        PublishedAt = now;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Status = OutboxMessageStatus.Failed;
        RetryCount++;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }

    public void MarkProjected(DateTimeOffset now) => ProjectedAt = now;
}
