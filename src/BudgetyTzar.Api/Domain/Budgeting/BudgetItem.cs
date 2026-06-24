using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Api;

public sealed class BudgetItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public required string Name { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BudgetItem Create(Guid budgetId, string name) =>
        new()
        {
            BudgetId = budgetId,
            Name = name.Trim()
        };

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetItemCreated",
            BudgetId,
            nameof(BudgetItem),
            Id,
            $"Created budget item {Name}.",
            Payload: new BudgetItemCreatedPayload(BudgetId, Id, Name));

    public DomainEvent Archive(DateTimeOffset archivedAt)
    {
        IsArchived = true;
        ArchivedAt = archivedAt;
        return new DomainEvent(
            "BudgetItemArchived",
            BudgetId,
            nameof(BudgetItem),
            Id,
            $"Archived budget item {Name}.",
            Payload: new BudgetItemArchivedPayload(BudgetId, Id, Name, archivedAt));
    }

    public bool CanAcceptActivityOn(DateOnly activityDate)
    {
        if (!IsArchived || ArchivedAt is null)
        {
            return true;
        }

        return activityDate <= DateOnly.FromDateTime(ArchivedAt.Value.UtcDateTime);
    }
}
