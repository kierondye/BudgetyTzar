namespace BudgetyTzar.Api;

public sealed class Budget
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static Budget Create(string name, string currency) =>
        new()
        {
            Name = name.Trim(),
            Currency = currency
        };

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetCreated",
            Id,
            null,
            nameof(Budget),
            Id,
            $"Created budget {Name}.");
}
