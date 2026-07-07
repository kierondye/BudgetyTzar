using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class InMemoryBudgetRepository
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, Budget> budgetsById = [];
    private readonly List<Guid> budgetIds = [];

    public AddBudgetResult Add(Budget budget)
    {
        lock (syncRoot)
        {
            if (HasBudgetNamedCore(budget.Name, exceptBudgetId: null))
            {
                return new AddBudgetResult.DuplicateName();
            }

            budgetsById[budget.BudgetId] = budget;
            budgetIds.Add(budget.BudgetId);

            return new AddBudgetResult.Added(budget);
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
            if (HasBudgetNamedCore(updatedBudget.Name, budgetId))
            {
                return new BudgetUpdateResult.Conflict();
            }

            budgetsById[budgetId] = updatedBudget;
            return new BudgetUpdateResult.Updated(updatedBudget);
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
        return budgetsById.Values.Any(budget =>
            budget.BudgetId != exceptBudgetId
            && string.Equals(budget.Name, name, StringComparison.Ordinal));
    }
}

public abstract record AddBudgetResult
{
    public sealed record Added(Budget Budget) : AddBudgetResult;

    public sealed record DuplicateName : AddBudgetResult;
}

public abstract record BudgetUpdateResult
{
    public sealed record Updated(Budget Budget) : BudgetUpdateResult;

    public sealed record NotFound : BudgetUpdateResult;

    public sealed record Conflict : BudgetUpdateResult;
}

public sealed record BudgetItemReference(Guid BudgetId, CurrencyCode BudgetCurrency, BudgetItem BudgetItem);
