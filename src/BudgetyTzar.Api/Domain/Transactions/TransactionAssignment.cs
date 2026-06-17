namespace BudgetyTzar.Api;

public sealed class TransactionAssignment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public Guid BudgetLineId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static TransactionAssignment Create(Guid transactionId, Guid budgetLineId, decimal amount) =>
        new()
        {
            TransactionId = transactionId,
            BudgetLineId = budgetLineId,
            Amount = MoneyAmount.Positive(amount).Value
        };
}
