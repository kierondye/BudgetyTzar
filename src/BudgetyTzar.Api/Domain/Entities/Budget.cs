using System.Collections.Immutable;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Domain.Entities;

public sealed class Budget
{
    private Budget(
        Guid budgetId,
        string name,
        CurrencyCode currency,
        ImmutableArray<BudgetItem> budgetItems)
    {
        BudgetId = budgetId;
        Name = name;
        Currency = currency;
        BudgetItems = budgetItems;
    }

    public Guid BudgetId { get; }

    public string Name { get; }

    public CurrencyCode Currency { get; }

    public ImmutableArray<BudgetItem> BudgetItems { get; }

    public static Budget Create(Guid budgetId, string name, CurrencyCode currency)
    {
        EnsureIdentity(budgetId, nameof(budgetId));
        var normalizedName = NormalizeName(name, nameof(name));

        return new Budget(budgetId, normalizedName, currency, []);
    }

    public Budget Rename(string name)
    {
        var normalizedName = NormalizeName(name, nameof(name));

        return new Budget(BudgetId, normalizedName, Currency, BudgetItems);
    }

    public Budget AddBudgetItem(
        Guid budgetItemId,
        string name,
        BudgetItemKind kind,
        PositiveMoneyAmount plannedAmount)
    {
        EnsureIdentity(budgetItemId, nameof(budgetItemId));
        EnsureNoBudgetItemWithName(name);

        var budgetItem = BudgetItem.Create(budgetItemId, name, kind, plannedAmount);
        return new Budget(BudgetId, Name, Currency, BudgetItems.Add(budgetItem));
    }

    public Budget RenameBudgetItem(Guid budgetItemId, string name)
    {
        EnsureNoBudgetItemWithName(name, budgetItemId);

        var budgetItem = RequireBudgetItem(budgetItemId);
        var renamedBudgetItem = budgetItem.Rename(name);

        return ReplaceBudgetItem(budgetItemId, renamedBudgetItem);
    }

    public Budget ChangeBudgetItemPlannedAmount(Guid budgetItemId, PositiveMoneyAmount plannedAmount)
    {
        var budgetItem = RequireBudgetItem(budgetItemId);
        var updatedBudgetItem = budgetItem.ChangePlannedAmount(plannedAmount);

        return ReplaceBudgetItem(budgetItemId, updatedBudgetItem);
    }

    public Budget RemoveBudgetItem(Guid budgetItemId)
    {
        RequireBudgetItem(budgetItemId);

        var budgetItems = BudgetItems.RemoveAll(budgetItem => budgetItem.BudgetItemId == budgetItemId);
        return new Budget(BudgetId, Name, Currency, budgetItems);
    }

    public BudgetItem? GetBudgetItem(Guid budgetItemId)
    {
        return BudgetItems.SingleOrDefault(budgetItem => budgetItem.BudgetItemId == budgetItemId);
    }

    public bool HasBudgetItemNamed(string name, Guid? exceptBudgetItemId = null)
    {
        var normalizedName = NormalizeName(name, nameof(name));

        return BudgetItems.Any(budgetItem =>
            budgetItem.BudgetItemId != exceptBudgetItemId
            && string.Equals(budgetItem.Name, normalizedName, StringComparison.Ordinal));
    }

    private Budget ReplaceBudgetItem(Guid budgetItemId, BudgetItem budgetItem)
    {
        var budgetItems = BudgetItems
            .Select(existingBudgetItem => existingBudgetItem.BudgetItemId == budgetItemId
                ? budgetItem
                : existingBudgetItem)
            .ToImmutableArray();

        return new Budget(BudgetId, Name, Currency, budgetItems);
    }

    private BudgetItem RequireBudgetItem(Guid budgetItemId)
    {
        return GetBudgetItem(budgetItemId)
            ?? throw new InvalidOperationException("Budget item does not belong to this budget.");
    }

    private void EnsureNoBudgetItemWithName(string name, Guid? exceptBudgetItemId = null)
    {
        if (HasBudgetItemNamed(name, exceptBudgetItemId))
        {
            throw new InvalidOperationException("Budget item names must be unique within a budget.");
        }
    }

    private static void EnsureIdentity(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Identity must not be empty.", parameterName);
        }
    }

    private static string NormalizeName(string name, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", parameterName);
        }

        return name.Trim();
    }
}
