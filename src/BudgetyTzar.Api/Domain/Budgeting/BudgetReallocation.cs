namespace BudgetyTzar.Api;

public sealed class BudgetReallocation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public Guid BudgetPeriodId { get; set; }
    public Guid FromBudgetLineId { get; set; }
    public Guid ToBudgetLineId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public required string Reason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BudgetReallocation Create(Guid budgetPeriodId, Guid fromBudgetLineId, Guid toBudgetLineId, decimal amount, string reason) =>
        new()
        {
            BudgetPeriodId = budgetPeriodId,
            FromBudgetLineId = fromBudgetLineId,
            ToBudgetLineId = toBudgetLineId,
            Amount = MoneyAmount.Positive(amount).Value,
            Reason = reason.Trim(),
            Notes = reason.Trim()
        };

    public static BudgetReallocation Create(Guid budgetId, DateOnly date, string? notes) =>
        new()
        {
            BudgetId = budgetId,
            Date = date,
            Amount = 0m,
            Reason = notes?.Trim() ?? string.Empty,
            Notes = notes?.Trim()
        };

    public DomainEvent RecordedEvent(Guid budgetId) =>
        new(
            "BudgetReallocationRecorded",
            budgetId,
            BudgetPeriodId == Guid.Empty ? null : BudgetPeriodId,
            nameof(BudgetReallocation),
            Id,
            $"Reallocated {Amount} from budget line {FromBudgetLineId} to budget line {ToBudgetLineId}: {Reason}");
}
