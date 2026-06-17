namespace BudgetyTzar.Api;

public sealed class BudgetAdjustment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetPeriodId { get; set; }
    public Guid BudgetLineId { get; set; }
    public decimal Amount { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BudgetAdjustment Create(Guid budgetPeriodId, Guid budgetLineId, decimal amount, string reason) =>
        new()
        {
            BudgetPeriodId = budgetPeriodId,
            BudgetLineId = budgetLineId,
            Amount = MoneyAmount.NonZero(amount).Value,
            Reason = reason.Trim()
        };

    public DomainEvent RecordedEvent(Guid budgetId, string budgetLineName) =>
        new(
            "BudgetAdjustmentRecorded",
            budgetId,
            BudgetPeriodId,
            nameof(BudgetAdjustment),
            Id,
            $"Recorded adjustment {Amount} for budget line {budgetLineName}: {Reason}");
}
