using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Api;

public sealed class Budget
{
    public const string DuplicateBudgetItemNameMessage = "A budget item with this name already exists in this budget.";

    private readonly List<BudgetItem> items = [];

    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyCollection<BudgetItem> Items => items;

    public static Budget Create(string name, string currency) =>
        new()
        {
            Name = name.Trim(),
            Currency = new Currency(currency).Value
        };

    internal void LoadItems(IReadOnlyCollection<BudgetItem> budgetItems)
    {
        items.Clear();
        foreach (var item in budgetItems)
        {
            if (item.BudgetId != Id)
            {
                throw new InvalidOperationException("Budget items must belong to the budget.");
            }

            items.Add(item);
        }
    }

    public string? ValidateBudgetItemName(string name)
    {
        var trimmedName = name.Trim();
        return Items.Any(x => x.Name == trimmedName) ? DuplicateBudgetItemNameMessage : null;
    }

    public BudgetItem CreateBudgetItem(string name, BudgetItemKind kind)
    {
        var validationError = ValidateBudgetItemName(name);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        var item = BudgetItem.Create(Id, name, kind);
        items.Add(item);
        return item;
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
