using BudgetyTzar.Api.Contracts.Events;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace BudgetyTzar.Api;

public abstract record CreateBudgetItemResult
{
    private CreateBudgetItemResult()
    {
    }

    public sealed record Success(Budget Budget, BudgetItem Item) : CreateBudgetItemResult;

    public sealed record DuplicateName(string Error) : CreateBudgetItemResult;
}

public sealed class Budget
{
    public const string DuplicateBudgetItemNameMessage = "A budget item with this name already exists in this budget.";

    private readonly IReadOnlyCollection<BudgetItem> items;

    private Budget()
    {
        items = ToReadOnlyCollection([]);
    }

    [JsonConstructor]
    internal Budget(
        Guid id,
        string name,
        string currency,
        DateTimeOffset createdAt,
        IReadOnlyCollection<BudgetItem> items)
    {
        Id = id;
        Name = name;
        Currency = currency;
        CreatedAt = createdAt;
        this.items = ToReadOnlyCollection(items);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public IReadOnlyCollection<BudgetItem> Items => items;

    public static Budget Create(string name, string currency) =>
        new(
            Guid.NewGuid(),
            name.Trim(),
            new Currency(currency).Value,
            DateTimeOffset.UtcNow,
            []);

    internal Budget WithItems(IReadOnlyCollection<BudgetItem> budgetItems)
    {
        foreach (var item in budgetItems)
        {
            if (item.BudgetId != Id)
            {
                throw new InvalidOperationException("Budget items must belong to the budget.");
            }
        }

        return new Budget(Id, Name, Currency, CreatedAt, budgetItems.ToArray());
    }

    public string? ValidateBudgetItemName(string name)
    {
        var trimmedName = name.Trim();
        return Items.Any(x => x.Name == trimmedName) ? DuplicateBudgetItemNameMessage : null;
    }

    public CreateBudgetItemResult CreateBudgetItem(string name, BudgetItemKind kind)
    {
        var validationError = ValidateBudgetItemName(name);
        if (validationError is not null)
        {
            return new CreateBudgetItemResult.DuplicateName(validationError);
        }

        var item = BudgetItem.Create(Id, name, kind);
        return new CreateBudgetItemResult.Success(
            new Budget(Id, Name, Currency, CreatedAt, items.Append(item).ToArray()),
            item);
    }

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetCreated",
            Id,
            nameof(Budget),
            Id,
            $"Created budget {Name}.",
            Payload: new BudgetCreatedPayload(Id, Name, Currency));

    private static ReadOnlyCollection<BudgetItem> ToReadOnlyCollection(IEnumerable<BudgetItem> items) =>
        Array.AsReadOnly(items.ToArray());
}
