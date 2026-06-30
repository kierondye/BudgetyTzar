using BudgetyTzar.Api;
using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Tests;

public sealed class BudgetItemTests
{
    [Fact]
    public void BudgetItemDoesNotExposePublicMutationOrConstruction()
    {
        var publicConstructors = typeof(BudgetItem).GetConstructors();
        var mutableProperties = typeof(BudgetItem)
            .GetProperties()
            .Where(x => x.SetMethod?.IsPublic == true)
            .Select(x => x.Name)
            .ToList();

        Assert.Empty(publicConstructors);
        Assert.Empty(mutableProperties);
    }

    [Fact]
    public void BudgetItemArchiveReturnsArchivedItemWithoutMutatingOriginal()
    {
        var budgetId = Guid.NewGuid();
        var item = BudgetItem.Create(budgetId, "Old category", BudgetItemKind.Consumption);
        var archivedAt = DateTimeOffset.UtcNow;

        var archivedItem = item.Archive(archivedAt);

        Assert.False(item.IsArchived);
        Assert.Null(item.ArchivedAt);
        Assert.True(archivedItem.IsArchived);
        Assert.Equal(archivedAt, archivedItem.ArchivedAt);
        Assert.Equal(item.Id, archivedItem.Id);
        Assert.Equal(item.BudgetId, archivedItem.BudgetId);
        Assert.Equal(item.Name, archivedItem.Name);
        Assert.Equal(item.Kind, archivedItem.Kind);
        Assert.Equal(item.CreatedAt, archivedItem.CreatedAt);
    }

    [Fact]
    public void BudgetItemArchivedEventPreservesPayload()
    {
        var budgetId = Guid.NewGuid();
        var item = BudgetItem.Create(budgetId, "Old category", BudgetItemKind.Consumption);
        var archivedAt = DateTimeOffset.UtcNow;
        var archivedItem = item.Archive(archivedAt);

        var domainEvent = archivedItem.ArchivedEvent();

        Assert.Equal("BudgetItemArchived", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(archivedItem.Id, domainEvent.EntityId);
        var payload = Assert.IsType<BudgetItemArchivedPayload>(domainEvent.Payload);
        Assert.Equal(BudgetItemKind.Consumption, payload.Kind);
        Assert.Equal(archivedAt, payload.ArchivedAt);
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
