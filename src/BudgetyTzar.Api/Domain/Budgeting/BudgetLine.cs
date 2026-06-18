namespace BudgetyTzar.Api;

public sealed class BudgetLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public required string Name { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BudgetLine Create(Guid budgetId, string name) =>
        new()
        {
            BudgetId = budgetId,
            Name = name.Trim()
        };

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetLineCreated",
            BudgetId,
            nameof(BudgetLine),
            Id,
            $"Created budget line {Name}.",
            Payload: new
            {
                BudgetId,
                BudgetItemId = Id,
                Name
            });

    public DomainEvent Archive()
    {
        IsArchived = true;
        return new DomainEvent(
            "BudgetLineArchived",
            BudgetId,
            nameof(BudgetLine),
            Id,
            $"Archived budget line {Name}.",
            Payload: new
            {
                BudgetId,
                BudgetItemId = Id,
                Name
            });
    }
}
