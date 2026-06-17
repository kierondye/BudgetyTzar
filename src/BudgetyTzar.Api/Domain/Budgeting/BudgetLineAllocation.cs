namespace BudgetyTzar.Api;

public sealed class BudgetLineAllocation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetPeriodId { get; set; }
    public Guid BudgetLineId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BudgetLineAllocation Create(Guid budgetPeriodId, Guid budgetLineId, decimal amount) =>
        new()
        {
            BudgetPeriodId = budgetPeriodId,
            BudgetLineId = budgetLineId,
            Amount = MoneyAmount.Positive(amount).Value
        };
}
