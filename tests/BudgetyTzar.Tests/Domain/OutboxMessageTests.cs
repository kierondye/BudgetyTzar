using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class OutboxMessageTests
{
    [Fact]
    public void OutboxPublishingLeaseIsClearedOnPublishOrFailure()
    {
        var message = new OutboxMessage
        {
            Topic = "budgetytzar.budgeting.events",
            EventType = "budgetytzar.budgeting.budget-created.v1",
            AggregateId = Guid.NewGuid(),
            AggregateType = nameof(Budget),
            BudgetId = Guid.NewGuid(),
            EnvelopeJson = "{}"
        };
        var lockId = Guid.NewGuid();
        var lockedAt = DateTimeOffset.UtcNow;

        message.Status = OutboxMessageStatus.Publishing;
        message.PublishingLockId = lockId;
        message.PublishingLockedAt = lockedAt;
        Assert.Equal(OutboxMessageStatus.Publishing, message.Status);
        Assert.Equal(lockId, message.PublishingLockId);
        Assert.Equal(lockedAt, message.PublishingLockedAt);

        message.MarkFailed("temporary failure");

        Assert.Equal(OutboxMessageStatus.Failed, message.Status);
        Assert.Null(message.PublishingLockId);
        Assert.Null(message.PublishingLockedAt);

        message.Status = OutboxMessageStatus.Publishing;
        message.PublishingLockId = lockId;
        message.PublishingLockedAt = lockedAt;
        message.MarkPublished(DateTimeOffset.UtcNow);

        Assert.Equal(OutboxMessageStatus.Published, message.Status);
        Assert.Null(message.PublishingLockId);
        Assert.Null(message.PublishingLockedAt);
    }
}
