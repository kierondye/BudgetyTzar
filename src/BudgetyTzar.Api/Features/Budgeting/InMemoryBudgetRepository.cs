using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class InMemoryBudgetRepository
{
    private readonly object syncRoot;
    private readonly Dictionary<Guid, Budget> budgetsById = [];
    private readonly Dictionary<Guid, long> budgetVersionsById = [];
    private readonly Dictionary<NormalizedName, Guid> budgetIdsByName = [];
    private readonly List<Guid> budgetIds = [];

    public InMemoryBudgetRepository(InMemoryDataStoreLock? dataStoreLock = null)
    {
        syncRoot = (dataStoreLock ?? new InMemoryDataStoreLock()).SyncRoot;
    }

    public BudgetSaveResult Save(Budget budget)
    {
        lock (syncRoot)
        {
            return SaveCore(budget);
        }
    }

    public BudgetSaveResult SaveRemovalIfBudgetItemHasNoAllocations(
        EntityState<Budget> budgetState,
        Guid budgetItemId,
        Func<Guid, bool> hasAllocationForBudgetItem)
    {
        lock (syncRoot)
        {
            if (hasAllocationForBudgetItem(budgetItemId))
            {
                return new BudgetSaveResult.BudgetItemHasAllocations();
            }

            return SaveCore(budgetState.Value, budgetState.Version);
        }
    }

    public BudgetSaveResult Save(EntityState<Budget> budgetState)
    {
        lock (syncRoot)
        {
            return SaveCore(budgetState.Value, budgetState.Version);
        }
    }

    public bool HasBudgetNamed(NormalizedName name, Guid? exceptBudgetId = null)
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

    public EntityState<Budget>? Get(Guid budgetId)
    {
        lock (syncRoot)
        {
            return budgetsById.TryGetValue(budgetId, out var budget)
                ? new EntityState<Budget>(budget, budgetVersionsById[budgetId])
                : null;
        }
    }

    public BudgetItem? GetBudgetItem(Guid budgetId, Guid budgetItemId)
    {
        lock (syncRoot)
        {
            if (!budgetsById.TryGetValue(budgetId, out var budget))
            {
                return null;
            }

            return budget.GetBudgetItem(budgetItemId) is GetBudgetItemResult.Found found
                ? found.BudgetItem
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

    private bool HasBudgetNamedCore(NormalizedName name, Guid? exceptBudgetId)
    {
        return budgetIdsByName.TryGetValue(name, out var budgetId)
            && budgetId != exceptBudgetId;
    }

    private BudgetSaveResult SaveCore(Budget budget, long? expectedVersion = null)
    {
        var hasExistingBudget = budgetsById.TryGetValue(budget.BudgetId, out var existingBudget);

        if (expectedVersion.HasValue && !hasExistingBudget)
        {
            return new BudgetSaveResult.NotFound();
        }

        if (expectedVersion.HasValue
            && budgetVersionsById[budget.BudgetId] != expectedVersion.Value)
        {
            return new BudgetSaveResult.StaleState();
        }

        if (!expectedVersion.HasValue && hasExistingBudget)
        {
            return new BudgetSaveResult.DuplicateIdentity();
        }

        if (budgetIdsByName.TryGetValue(budget.Name, out var existingBudgetId)
            && existingBudgetId != budget.BudgetId)
        {
            return new BudgetSaveResult.DuplicateName();
        }

        if (hasExistingBudget)
        {
            budgetIdsByName.Remove(existingBudget!.Name);
        }
        else
        {
            budgetIds.Add(budget.BudgetId);
        }

        budgetsById[budget.BudgetId] = budget;
        budgetVersionsById[budget.BudgetId] = hasExistingBudget
            ? budgetVersionsById[budget.BudgetId] + 1
            : 1;
        budgetIdsByName[budget.Name] = budget.BudgetId;

        return new BudgetSaveResult.Saved(budget);
    }
}

public abstract record BudgetSaveResult
{
    public sealed record Saved(Budget Budget) : BudgetSaveResult;

    public sealed record NotFound : BudgetSaveResult;

    public sealed record DuplicateIdentity : BudgetSaveResult;

    public sealed record DuplicateName : BudgetSaveResult;

    public sealed record StaleState : BudgetSaveResult;

    public sealed record BudgetItemHasAllocations : BudgetSaveResult;
}

public sealed record EntityState<T>(T Value, long Version)
{
    public EntityState<T> Update(T value)
    {
        return this with { Value = value };
    }
}

public sealed record BudgetItemReference(Guid BudgetId, CurrencyCode BudgetCurrency, BudgetItem BudgetItem);
