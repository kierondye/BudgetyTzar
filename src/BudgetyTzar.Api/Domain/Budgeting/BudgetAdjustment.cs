using System.Text.Json.Serialization;
using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Api;

[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum BudgetAdjustmentType
{
    Debit,
    Credit
}

public sealed class BudgetAdjustment
{
    private BudgetAdjustment()
    {
    }

    private BudgetAdjustment(
        Guid id,
        Guid budgetId,
        Guid budgetItemId,
        Guid? reallocationId,
        DateOnly date,
        decimal amount,
        BudgetAdjustmentType type,
        string reason,
        string? notes,
        decimal legacySignedAmount,
        DateTimeOffset createdAt)
    {
        Id = id;
        BudgetId = budgetId;
        BudgetItemId = budgetItemId;
        ReallocationId = reallocationId;
        Date = date;
        Amount = amount;
        Type = type;
        Reason = reason;
        Notes = notes;
        LegacySignedAmount = legacySignedAmount;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid BudgetId { get; private set; }
    public Guid BudgetItemId { get; private set; }
    public Guid? ReallocationId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Amount { get; private set; }
    public BudgetAdjustmentType Type { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public decimal LegacySignedAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public static BudgetAdjustment Create(
        Guid budgetId,
        Guid budgetItemId,
        decimal amount,
        BudgetAdjustmentType type,
        DateOnly date,
        string? notes,
        Guid? reallocationId = null) =>
        Create(budgetId, budgetItemId, PositiveMoneyAmount.Require(amount), type, date, notes, reallocationId);

    internal static BudgetAdjustment Create(
        Guid budgetId,
        Guid budgetItemId,
        PositiveMoneyAmount amount,
        BudgetAdjustmentType type,
        DateOnly date,
        string? notes,
        Guid? reallocationId = null)
    {
        var trimmedNotes = notes?.Trim();
        return new BudgetAdjustment(
            Guid.NewGuid(),
            budgetId,
            budgetItemId,
            reallocationId,
            date,
            amount.Value,
            type,
            trimmedNotes ?? string.Empty,
            trimmedNotes,
            type == BudgetAdjustmentType.Credit ? amount.Value : -amount.Value,
            DateTimeOffset.UtcNow);
    }

    public decimal SignedPlannedAmount() =>
        Type == BudgetAdjustmentType.Credit ? Amount : -Amount;

    public DomainEvent RecordedEvent(string budgetItemName) =>
        new(
            "BudgetAdjustmentRecorded",
            BudgetId,
            nameof(BudgetAdjustment),
            Id,
            $"Recorded {Type} adjustment {Amount} for budget item {budgetItemName}: {Reason}",
            Payload: new BudgetAdjustmentRecordedPayload(Id, BudgetId, BudgetItemId, Amount, Type, Date, Notes));
}
