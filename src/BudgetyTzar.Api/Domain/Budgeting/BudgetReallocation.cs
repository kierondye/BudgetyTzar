namespace BudgetyTzar.Api;

public sealed class BudgetReallocation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public Guid FromBudgetItemId { get; set; }
    public Guid ToBudgetItemId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public required string Reason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

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
            nameof(BudgetReallocation),
            Id,
            $"Recorded budget reallocation {Id}: {Reason}",
            Payload: new
            {
                BudgetReallocationId = Id,
                BudgetId = budgetId,
                Date,
                Notes
            });
}
