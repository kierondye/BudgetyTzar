using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Identity;

namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class InMemoryBudgetRepository
{
    private readonly InMemoryDataStore store;

    public InMemoryBudgetRepository(InMemoryDataStore? store = null)
    {
        this.store = store ?? new InMemoryDataStore();
    }

    public BudgetSaveResult Save(Budget budget)
    {
        return Save(ApplicationUserId.DefaultTestUser, budget);
    }

    public BudgetSaveResult Save(ApplicationUserId ownerId, Budget budget)
    {
        lock (store.SyncRoot)
        {
            return SaveCore(ownerId, budget);
        }
    }

    public BudgetSaveResult Save(EntityState<Budget> budgetState)
    {
        return Save(ApplicationUserId.DefaultTestUser, budgetState);
    }

    public BudgetSaveResult Save(ApplicationUserId ownerId, EntityState<Budget> budgetState)
    {
        if (budgetState is not InMemoryEntityState<Budget> inMemoryState)
        {
            return new BudgetSaveResult.InvalidState();
        }

        lock (store.SyncRoot)
        {
            if (inMemoryState.OwnerId != ownerId)
            {
                return new BudgetSaveResult.NotFound();
            }

            return SaveCore(ownerId, inMemoryState.Value, inMemoryState.Version);
        }
    }

    public bool HasBudgetNamed(NormalizedName name, Guid? exceptBudgetId = null)
    {
        return HasBudgetNamed(ApplicationUserId.DefaultTestUser, name, exceptBudgetId);
    }

    public bool HasBudgetNamed(ApplicationUserId ownerId, NormalizedName name, Guid? exceptBudgetId = null)
    {
        lock (store.SyncRoot)
        {
            return HasBudgetNamedCore(ownerId, name, exceptBudgetId);
        }
    }

    public IReadOnlyList<Budget> GetAll()
    {
        return GetAll(ApplicationUserId.DefaultTestUser);
    }

    public IReadOnlyList<Budget> GetAll(ApplicationUserId ownerId)
    {
        lock (store.SyncRoot)
        {
            return store.BudgetIds
                .Where(budgetId => store.BudgetOwnersById[budgetId] == ownerId)
                .Select(budgetId => store.BudgetsById[budgetId])
                .ToList();
        }
    }

    public EntityState<Budget>? Get(Guid budgetId)
    {
        return Get(ApplicationUserId.DefaultTestUser, budgetId);
    }

    public EntityState<Budget>? Get(ApplicationUserId ownerId, Guid budgetId)
    {
        lock (store.SyncRoot)
        {
            return store.BudgetsById.TryGetValue(budgetId, out var budget)
                && store.BudgetOwnersById[budgetId] == ownerId
                ? new InMemoryEntityState<Budget>(ownerId, budget, store.BudgetVersionsById[budgetId])
                : null;
        }
    }

    public BudgetItem? GetBudgetItem(Guid budgetId, Guid budgetItemId)
    {
        return GetBudgetItem(ApplicationUserId.DefaultTestUser, budgetId, budgetItemId);
    }

    public BudgetItem? GetBudgetItem(ApplicationUserId ownerId, Guid budgetId, Guid budgetItemId)
    {
        lock (store.SyncRoot)
        {
            if (!store.BudgetsById.TryGetValue(budgetId, out var budget)
                || store.BudgetOwnersById[budgetId] != ownerId)
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
        return GetBudgetItemReference(ApplicationUserId.DefaultTestUser, budgetItemId);
    }

    public BudgetItemReference? GetBudgetItemReference(ApplicationUserId ownerId, Guid budgetItemId)
    {
        lock (store.SyncRoot)
        {
            foreach (var budgetId in store.BudgetIds)
            {
                if (store.BudgetOwnersById[budgetId] != ownerId)
                {
                    continue;
                }

                var budget = store.BudgetsById[budgetId];
                var budgetItem = budget.BudgetItems.SingleOrDefault(budgetItem => budgetItem.BudgetItemId == budgetItemId);

                if (budgetItem is not null)
                {
                    return new BudgetItemReference(budget.BudgetId, budget.Currency, budgetItem);
                }
            }

            return null;
        }
    }

    private bool HasBudgetNamedCore(ApplicationUserId ownerId, NormalizedName name, Guid? exceptBudgetId)
    {
        return store.BudgetIdsByOwnerAndName.TryGetValue(new OwnedBudgetName(ownerId, name), out var budgetId)
            && budgetId != exceptBudgetId;
    }

    private BudgetSaveResult SaveCore(ApplicationUserId ownerId, Budget budget, long? expectedVersion = null)
    {
        var hasExistingBudget = store.BudgetsById.TryGetValue(budget.BudgetId, out var existingBudget);
        var hasDifferentOwner = hasExistingBudget && store.BudgetOwnersById[budget.BudgetId] != ownerId;

        if (expectedVersion.HasValue && (!hasExistingBudget || hasDifferentOwner))
        {
            return new BudgetSaveResult.NotFound();
        }

        if (!expectedVersion.HasValue && hasExistingBudget)
        {
            return new BudgetSaveResult.DuplicateIdentity();
        }

        if (expectedVersion.HasValue
            && store.BudgetVersionsById[budget.BudgetId] != expectedVersion.Value)
        {
            return new BudgetSaveResult.StaleState();
        }

        if (store.BudgetIdsByOwnerAndName.TryGetValue(new OwnedBudgetName(ownerId, budget.Name), out var existingBudgetId)
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
            store.BudgetIdsByOwnerAndName.Remove(new OwnedBudgetName(ownerId, existingBudget!.Name));
        }
        else
        {
            store.BudgetIds.Add(budget.BudgetId);
            store.BudgetOwnersById[budget.BudgetId] = ownerId;
        }

        store.BudgetsById[budget.BudgetId] = budget;
        store.BudgetVersionsById[budget.BudgetId] = hasExistingBudget
            ? store.BudgetVersionsById[budget.BudgetId] + 1
            : 1;
        store.BudgetIdsByOwnerAndName[new OwnedBudgetName(ownerId, budget.Name)] = budget.BudgetId;

        return new BudgetSaveResult.Saved(budget);
    }

    private bool RemovedBudgetItemHasAllocations(Budget existingBudget, Budget updatedBudget)
    {
        var removedBudgetItemIds = existingBudget.BudgetItems
            .Select(budgetItem => budgetItem.BudgetItemId)
            .Except(updatedBudget.BudgetItems.Select(budgetItem => budgetItem.BudgetItemId))
            .ToHashSet();

        return removedBudgetItemIds.Count > 0
            && store.AllocationsByTransactionId.Any(allocation =>
                store.AllocationOwnersByTransactionId[allocation.Key] == store.BudgetOwnersById[existingBudget.BudgetId]
                && removedBudgetItemIds.Contains(allocation.Value.BudgetItemId));
    }

    private sealed class InMemoryEntityState<T>(ApplicationUserId ownerId, T value, long version) : EntityState<T>(value)
    {
        public ApplicationUserId OwnerId { get; } = ownerId;

        public long Version { get; } = version;

        public override EntityState<T> Update(T value)
        {
            return new InMemoryEntityState<T>(OwnerId, value, Version);
        }
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

    public sealed record InvalidState : BudgetSaveResult;
}

public abstract class EntityState<T>
{
    protected EntityState(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Value = value;
    }

    public T Value { get; }

    public abstract EntityState<T> Update(T value);
}

public sealed record BudgetItemReference(Guid BudgetId, CurrencyCode BudgetCurrency, BudgetItem BudgetItem);
