namespace BudgetyTzar.Api;

public sealed class BudgetReallocation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetPeriodId { get; set; }
    public Guid FromBudgetLineId { get; set; }
    public Guid ToBudgetLineId { get; set; }
    public decimal Amount { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BudgetReallocation Create(Guid budgetPeriodId, Guid fromBudgetLineId, Guid toBudgetLineId, decimal amount, string reason) =>
        new()
        {
            BudgetPeriodId = budgetPeriodId,
            FromBudgetLineId = fromBudgetLineId,
            ToBudgetLineId = toBudgetLineId,
            Amount = MoneyAmount.Positive(amount).Value,
            Reason = reason.Trim()
        };

    public DomainEvent RecordedEvent(Guid budgetId) =>
        new(
            "BudgetReallocationRecorded",
            budgetId,
            BudgetPeriodId,
            nameof(BudgetReallocation),
            Id,
            $"Reallocated {Amount} from budget line {FromBudgetLineId} to budget line {ToBudgetLineId}: {Reason}");
}
