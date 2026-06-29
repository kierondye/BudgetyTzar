using BudgetyTzar.Api.Contracts.Events;
using System.Text.Json.Serialization;

namespace BudgetyTzar.Api;

[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum BudgetItemKind
{
    Funding = 1,
    Consumption = 2
}

public sealed class BudgetItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public required string Name { get; set; }
    public BudgetItemKind Kind { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BudgetItem Create(Guid budgetId, string name, BudgetItemKind kind) =>
        new()
        {
            BudgetId = budgetId,
            Name = name.Trim(),
            Kind = kind
        };

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetItemCreated",
            BudgetId,
            nameof(BudgetItem),
            Id,
            $"Created budget item {Name}.",
            Payload: new BudgetItemCreatedPayload(BudgetId, Id, Name, Kind));

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
            Payload: new BudgetItemArchivedPayload(BudgetId, Id, Name, Kind, archivedAt));
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
