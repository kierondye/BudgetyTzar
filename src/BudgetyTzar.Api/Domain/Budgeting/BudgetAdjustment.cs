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
    public Guid BudgetPeriodId { get; set; }
    public Guid BudgetLineId { get; set; }
    public Guid? ReallocationId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public BudgetAdjustmentType Type { get; set; }
    public required string Reason { get; set; }
    public string? Notes { get; set; }
    public decimal LegacySignedAmount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BudgetAdjustment Create(Guid budgetPeriodId, Guid budgetLineId, decimal amount, string reason) =>
        CreateLegacy(Guid.Empty, budgetPeriodId, budgetLineId, DateOnly.MinValue, amount, reason);

    public static BudgetAdjustment CreateLegacy(Guid budgetId, Guid budgetPeriodId, Guid budgetLineId, DateOnly date, decimal amount, string reason) =>
        new()
        {
            BudgetId = budgetId,
            BudgetPeriodId = budgetPeriodId,
            BudgetLineId = budgetLineId,
            Date = date,
            Amount = MoneyAmount.Positive(Math.Abs(MoneyAmount.NonZero(amount).Value)).Value,
            Type = amount < 0 ? BudgetAdjustmentType.Debit : BudgetAdjustmentType.Credit,
            Reason = reason.Trim(),
            Notes = reason.Trim(),
            LegacySignedAmount = amount
        };

    public static BudgetAdjustment Create(
        Guid budgetId,
        Guid budgetLineId,
        decimal amount,
        BudgetAdjustmentType type,
        DateOnly date,
        string? notes,
        Guid? budgetPeriodId = null,
        Guid? reallocationId = null) =>
        new()
        {
            BudgetId = budgetId,
            BudgetPeriodId = budgetPeriodId ?? Guid.Empty,
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
            BudgetPeriodId == Guid.Empty ? null : BudgetPeriodId,
            nameof(BudgetAdjustment),
            Id,
            $"Recorded {Type} adjustment {Amount} for budget line {budgetLineName}: {Reason}");
}
