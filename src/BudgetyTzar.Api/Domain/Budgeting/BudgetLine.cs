namespace BudgetyTzar.Api;

public enum BudgetLineDirection
{
    Debit,
    Credit
}

public enum BudgetLineRolloverType
{
    PeriodReset,
    Cumulative
}

public sealed class BudgetLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public required string Name { get; set; }
    public BudgetLineDirection Direction { get; set; }
    public BudgetLineRolloverType RolloverType { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BudgetLine Create(Guid budgetId, string name, BudgetLineDirection direction, BudgetLineRolloverType rolloverType) =>
        new()
        {
            BudgetId = budgetId,
            Name = name.Trim(),
            Direction = direction,
            RolloverType = rolloverType
        };

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetLineCreated",
            BudgetId,
            null,
            nameof(BudgetLine),
            Id,
            $"Created budget line {Name}.");

    public DomainEvent Archive()
    {
        IsArchived = true;
        return new DomainEvent(
            "BudgetLineArchived",
            BudgetId,
            null,
            nameof(BudgetLine),
            Id,
            $"Archived budget line {Name}.");
    }
}
