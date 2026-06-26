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
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public Guid BudgetItemId { get; set; }
    public Guid? ReallocationId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public BudgetAdjustmentType Type { get; set; }
    public required string Reason { get; set; }
    public string? Notes { get; set; }
    public decimal LegacySignedAmount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BudgetAdjustment Create(
        Guid budgetId,
        Guid budgetItemId,
        decimal amount,
        BudgetAdjustmentType type,
        DateOnly date,
        string? notes,
        Guid? reallocationId = null) =>
        new()
        {
            BudgetId = budgetId,
            BudgetItemId = budgetItemId,
            ReallocationId = reallocationId,
            Date = date,
            Amount = MoneyAmount.Positive(amount).Value,
            Type = type,
            Reason = notes?.Trim() ?? string.Empty,
            Notes = notes?.Trim(),
            LegacySignedAmount = type == BudgetAdjustmentType.Credit ? amount : -amount
        };

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
