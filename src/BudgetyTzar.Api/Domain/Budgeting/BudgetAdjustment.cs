using System.Text.Json.Serialization;

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
    public Guid BudgetLineId { get; set; }
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
        Guid budgetLineId,
        decimal amount,
        BudgetAdjustmentType type,
        DateOnly date,
        string? notes,
        Guid? reallocationId = null) =>
        new()
        {
            BudgetId = budgetId,
            BudgetLineId = budgetLineId,
            ReallocationId = reallocationId,
            Date = date,
            Amount = MoneyAmount.Positive(amount).Value,
            Type = type,
            Reason = notes?.Trim() ?? string.Empty,
            Notes = notes?.Trim(),
            LegacySignedAmount = type == BudgetAdjustmentType.Credit ? amount : -amount
        };

    public DomainEvent RecordedEvent(Guid budgetId, string budgetLineName) =>
        new(
            "BudgetAdjustmentRecorded",
            budgetId,
            nameof(BudgetAdjustment),
            Id,
            $"Recorded {Type} adjustment {Amount} for budget line {budgetLineName}: {Reason}",
            Payload: new
            {
                BudgetAdjustmentId = Id,
                BudgetId = budgetId,
                BudgetItemId = BudgetLineId,
                Amount,
                Direction = Type,
                Date,
                Notes
            });
}
