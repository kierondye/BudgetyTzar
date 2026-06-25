using BudgetyTzar.Api.Contracts.Events;

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
            Currency = new Currency(currency).Value
        };

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetCreated",
            Id,
            nameof(Budget),
            Id,
            $"Created budget {Name}.",
            Payload: new BudgetCreatedPayload(Id, Name, Currency));
}
