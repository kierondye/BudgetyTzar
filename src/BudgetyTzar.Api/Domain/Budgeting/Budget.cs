using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Api;

public sealed class Budget
{
    public const string DuplicateBudgetItemNameMessage = "A budget item with this name already exists in this budget.";

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

    public string? ValidateBudgetItemName(IReadOnlyCollection<BudgetItem> existingItems, string name)
    {
        var trimmedName = name.Trim();
        return existingItems.Any(x => x.Name == trimmedName) ? DuplicateBudgetItemNameMessage : null;
    }

    public BudgetItem CreateBudgetItem(IReadOnlyCollection<BudgetItem> existingItems, string name, BudgetItemKind kind)
    {
        var validationError = ValidateBudgetItemName(existingItems, name);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        return BudgetItem.Create(Id, name, kind);
    }

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetCreated",
            Id,
            nameof(Budget),
            Id,
            $"Created budget {Name}.",
            Payload: new BudgetCreatedPayload(Id, Name, Currency));
}
