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
    public const string ConsumptionBudgetItemBecameFundingMessage = "A consumption item must not become a funding source through budget adjustments.";
    public const string FundingBudgetItemBecameConsumptionMessage = "A funding item must not become a consumption item through budget adjustments.";

    private BudgetItem()
    {
    }

    private BudgetItem(
        Guid id,
        Guid budgetId,
        string name,
        BudgetItemKind kind,
        bool isArchived,
        DateTimeOffset? archivedAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        BudgetId = budgetId;
        Name = name;
        Kind = kind;
        IsArchived = isArchived;
        ArchivedAt = archivedAt;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid BudgetId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public BudgetItemKind Kind { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public static BudgetItem Create(Guid budgetId, string name, BudgetItemKind kind) =>
        new(
            Guid.NewGuid(),
            budgetId,
            name.Trim(),
            kind,
            isArchived: false,
            archivedAt: null,
            createdAt: DateTimeOffset.UtcNow);

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetItemCreated",
            BudgetId,
            nameof(BudgetItem),
            Id,
            $"Created budget item {Name}.",
            Payload: new BudgetItemCreatedPayload(BudgetId, Id, Name, Kind));

    public BudgetItem Archive(DateTimeOffset archivedAt) =>
        new(
            Id,
            BudgetId,
            Name,
            Kind,
            isArchived: true,
            archivedAt,
            createdAt: CreatedAt);

    public DomainEvent ArchivedEvent()
    {
        if (!IsArchived || ArchivedAt is null)
        {
            throw new InvalidOperationException("Only an archived budget item can produce an archived event.");
        }

        return new DomainEvent(
            "BudgetItemArchived",
            BudgetId,
            nameof(BudgetItem),
            Id,
            $"Archived budget item {Name}.",
            Payload: new BudgetItemArchivedPayload(BudgetId, Id, Name, Kind, ArchivedAt.Value));
    }

    public bool CanAcceptActivityOn(DateOnly activityDate)
    {
        if (!IsArchived || ArchivedAt is null)
        {
            return true;
        }

        return activityDate <= DateOnly.FromDateTime(ArchivedAt.Value.UtcDateTime);
    }

    public string? ValidateEffectivePlannedPosition(decimal plannedAmount) =>
        Kind switch
        {
            BudgetItemKind.Consumption when plannedAmount > 0 => ConsumptionBudgetItemBecameFundingMessage,
            BudgetItemKind.Funding when plannedAmount < 0 => FundingBudgetItemBecameConsumptionMessage,
            _ => null
        };
}
