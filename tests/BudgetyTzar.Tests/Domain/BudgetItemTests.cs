using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class BudgetItemTests
{
    [Fact]
    public void BudgetItemArchiveChangesStateAndProducesDomainEvent()
    {
        var budgetId = Guid.NewGuid();
        var item = BudgetItem.Create(budgetId, "Old category");
        var archivedAt = DateTimeOffset.UtcNow;

        var domainEvent = item.Archive(archivedAt);

        Assert.True(item.IsArchived);
        Assert.Equal(archivedAt, item.ArchivedAt);
        Assert.Equal("BudgetItemArchived", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(item.Id, domainEvent.EntityId);
    }
}
