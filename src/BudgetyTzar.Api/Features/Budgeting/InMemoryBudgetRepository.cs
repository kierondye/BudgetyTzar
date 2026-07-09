using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class InMemoryBudgetRepository : IBudgetRepository
{
    private readonly InMemoryDataStore store;

    public InMemoryBudgetRepository(InMemoryDataStore? store = null)
    {
        this.store = store ?? new InMemoryDataStore();
    }

    public BudgetSaveResult Save(Budget budget)
    {
        lock (store.SyncRoot)
        {
            return SaveCore(budget);
        }
    }

    public BudgetSaveResult Save(EntityState<Budget> budgetState)
    {
        if (budgetState is not InMemoryEntityState<Budget> inMemoryState)
        {
            return new BudgetSaveResult.InvalidState();
        }

        lock (store.SyncRoot)
        {
            return SaveCore(inMemoryState.Value, inMemoryState.Version);
        }
    }

    public bool HasBudgetNamed(NormalizedName name, Guid? exceptBudgetId = null)
    {
        lock (store.SyncRoot)
        {
            return HasBudgetNamedCore(name, exceptBudgetId);
        }
    }

    public IReadOnlyList<Budget> GetAll()
    {
        lock (store.SyncRoot)
        {
            return store.BudgetIds
                .Select(budgetId => store.BudgetsById[budgetId])
                .ToList();
        }
    }

    public EntityState<Budget>? Get(Guid budgetId)
    {
        lock (store.SyncRoot)
        {
            return store.BudgetsById.TryGetValue(budgetId, out var budget)
                ? new InMemoryEntityState<Budget>(budget, store.BudgetVersionsById[budgetId])
                : null;
        }
    }

    public BudgetItem? GetBudgetItem(Guid budgetId, Guid budgetItemId)
    {
        lock (store.SyncRoot)
        {
            if (!store.BudgetsById.TryGetValue(budgetId, out var budget))
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
        lock (store.SyncRoot)
        {
            foreach (var budget in store.BudgetsById.Values)
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
        return store.BudgetIdsByName.TryGetValue(name, out var budgetId)
            && budgetId != exceptBudgetId;
    }

    private BudgetSaveResult SaveCore(Budget budget, long? expectedVersion = null)
    {
        var hasExistingBudget = store.BudgetsById.TryGetValue(budget.BudgetId, out var existingBudget);

        if (expectedVersion.HasValue && !hasExistingBudget)
        {
            return new BudgetSaveResult.NotFound();
        }

        if (expectedVersion.HasValue
            && store.BudgetVersionsById[budget.BudgetId] != expectedVersion.Value)
        {
            return new BudgetSaveResult.StaleState();
        }

        if (!expectedVersion.HasValue && hasExistingBudget)
        {
            return new BudgetSaveResult.DuplicateIdentity();
        }

        if (store.BudgetIdsByName.TryGetValue(budget.Name, out var existingBudgetId)
            && existingBudgetId != budget.BudgetId)
        {
            return new BudgetSaveResult.DuplicateName();
        }

        if (hasExistingBudget && RemovedBudgetItemHasAllocations(existingBudget!, budget))
        {
            return new BudgetSaveResult.BudgetItemHasAllocations();
        }

        if (hasExistingBudget)
        {
            store.BudgetIdsByName.Remove(existingBudget!.Name);
        }
        else
        {
            store.BudgetIds.Add(budget.BudgetId);
        }

        store.BudgetsById[budget.BudgetId] = budget;
        store.BudgetVersionsById[budget.BudgetId] = hasExistingBudget
            ? store.BudgetVersionsById[budget.BudgetId] + 1
            : 1;
        store.BudgetIdsByName[budget.Name] = budget.BudgetId;

        return new BudgetSaveResult.Saved(budget);
    }

    private bool RemovedBudgetItemHasAllocations(Budget existingBudget, Budget updatedBudget)
    {
        var removedBudgetItemIds = existingBudget.BudgetItems
            .Select(budgetItem => budgetItem.BudgetItemId)
            .Except(updatedBudget.BudgetItems.Select(budgetItem => budgetItem.BudgetItemId))
            .ToHashSet();

        return removedBudgetItemIds.Count > 0
            && store.AllocationsByTransactionId.Values.Any(allocation => removedBudgetItemIds.Contains(allocation.BudgetItemId));
    }

    private sealed class InMemoryEntityState<T>(T value, long version) : EntityState<T>(value)
    {
        public long Version { get; } = version;

        public override EntityState<T> Update(T value)
        {
            return new InMemoryEntityState<T>(value, Version);
        }
    }
}
