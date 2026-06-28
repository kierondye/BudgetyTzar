using BudgetyTzar.Api;
using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Tests;

public sealed class BudgetItemTests
{
    [Fact]
    public void BudgetItemArchiveChangesStateAndProducesDomainEvent()
    {
        var budgetId = Guid.NewGuid();
        var item = BudgetItem.Create(budgetId, "Old category", BudgetItemKind.Consumption);
        var archivedAt = DateTimeOffset.UtcNow;

        var domainEvent = item.Archive(archivedAt);

        Assert.True(item.IsArchived);
        Assert.Equal(archivedAt, item.ArchivedAt);
        Assert.Equal("BudgetItemArchived", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(item.Id, domainEvent.EntityId);
    }

    [Fact]
    public void BudgetItemCreatedEventIncludesKind()
    {
        var budgetId = Guid.NewGuid();
        var item = BudgetItem.Create(budgetId, "Salary", BudgetItemKind.Funding);

        var domainEvent = item.CreatedEvent();

        var payload = Assert.IsType<BudgetItemCreatedPayload>(domainEvent.Payload);
        Assert.Equal(BudgetItemKind.Funding, payload.Kind);
    }
}
