using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class InMemoryBudgetRepository
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, Budget> budgetsById = [];
    private readonly Dictionary<string, Guid> budgetIdsByName = new(StringComparer.Ordinal);
    private readonly List<Guid> budgetIds = [];

    public BudgetSaveResult Save(Budget budget)
    {
        lock (syncRoot)
        {
            return SaveCore(budget);
        }
    }

    public BudgetUpdateResult TryUpdate(
        Guid budgetId,
        Func<Budget, Budget> update)
    {
        lock (syncRoot)
        {
            if (!budgetsById.TryGetValue(budgetId, out var budget))
            {
                return new BudgetUpdateResult.NotFound();
            }

            var updatedBudget = update(budget);
            return SaveCore(updatedBudget) switch
            {
                BudgetSaveResult.Conflict => new BudgetUpdateResult.Conflict(),
                BudgetSaveResult.Saved saved => new BudgetUpdateResult.Updated(saved.Budget),
                _ => throw new InvalidOperationException("Unexpected save budget result.")
            };
        }
    }

    public bool HasBudgetNamed(string name, Guid? exceptBudgetId = null)
    {
        lock (syncRoot)
        {
            return HasBudgetNamedCore(name, exceptBudgetId);
        }
    }

    public IReadOnlyList<Budget> GetAll()
    {
        lock (syncRoot)
        {
            return budgetIds
                .Select(budgetId => budgetsById[budgetId])
                .ToList();
        }
    }

    public Budget? Get(Guid budgetId)
    {
        lock (syncRoot)
        {
            return budgetsById.GetValueOrDefault(budgetId);
        }
    }

    public BudgetItem? GetBudgetItem(Guid budgetId, Guid budgetItemId)
    {
        lock (syncRoot)
        {
            return budgetsById.TryGetValue(budgetId, out var budget)
                ? budget.BudgetItems.SingleOrDefault(budgetItem => budgetItem.BudgetItemId == budgetItemId)
                : null;
        }
    }

    public BudgetItemReference? GetBudgetItemReference(Guid budgetItemId)
    {
        lock (syncRoot)
        {
            foreach (var budget in budgetsById.Values)
            {
                var budgetItem = budget.BudgetItems.SingleOrDefault(budgetItem => budgetItem.BudgetItemId == budgetItemId);

                if (budgetItem is not null)
                {
                    return new BudgetItemReference(budget.BudgetId, budget.Currency, budgetItem);
                }
            }

            return null;
        }
    }

    private bool HasBudgetNamedCore(string name, Guid? exceptBudgetId)
    {
        return budgetIdsByName.TryGetValue(NormalizeName(name), out var budgetId)
            && budgetId != exceptBudgetId;
    }

    private BudgetSaveResult SaveCore(Budget budget)
    {
        var normalizedName = NormalizeName(budget.Name);

        if (budgetIdsByName.TryGetValue(normalizedName, out var existingBudgetId)
            && existingBudgetId != budget.BudgetId)
        {
            return new BudgetSaveResult.Conflict();
        }

        if (budgetsById.TryGetValue(budget.BudgetId, out var existingBudget))
        {
            budgetIdsByName.Remove(NormalizeName(existingBudget.Name));
        }
        else
        {
            budgetIds.Add(budget.BudgetId);
        }

        budgetsById[budget.BudgetId] = budget;
        budgetIdsByName[normalizedName] = budget.BudgetId;

        return new BudgetSaveResult.Saved(budget);
    }

    private static string NormalizeName(string name)
    {
        return name.Trim();
    }
}

public abstract record BudgetSaveResult
{
    public sealed record Saved(Budget Budget) : BudgetSaveResult;

    public sealed record Conflict : BudgetSaveResult;
}

public abstract record BudgetUpdateResult
{
    public sealed record Updated(Budget Budget) : BudgetUpdateResult;

    public sealed record NotFound : BudgetUpdateResult;

    public sealed record Conflict : BudgetUpdateResult;
}

public sealed record BudgetItemReference(Guid BudgetId, CurrencyCode BudgetCurrency, BudgetItem BudgetItem);
