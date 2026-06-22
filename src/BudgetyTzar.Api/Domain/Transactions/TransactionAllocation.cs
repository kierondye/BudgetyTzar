namespace BudgetyTzar.Api;

public sealed class TransactionAllocation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public Guid BudgetItemId { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static TransactionAllocation Create(Guid transactionId, Guid budgetItemId, decimal amount, string? notes = null) =>
        new()
        {
            TransactionId = transactionId,
            BudgetItemId = budgetItemId,
            Amount = MoneyAmount.Positive(amount).Value,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
}
