using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Identity;

namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class InMemoryBudgetRepository
{
    private readonly InMemoryDataStore store;
    private readonly ICurrentUser currentUser;

    public InMemoryBudgetRepository()
        : this(new InMemoryDataStore(), CurrentUser.TestDefault)
    {
    }

    public InMemoryBudgetRepository(InMemoryDataStore store)
        : this(store, CurrentUser.TestDefault)
    {
    }

    public InMemoryBudgetRepository(InMemoryDataStore store, ICurrentUser currentUser)
    {
        this.store = store;
        this.currentUser = currentUser;
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
            return store.BudgetIdsByOwner.GetValueOrDefault(currentUser.UserId, [])
                .Select(budgetId => store.BudgetsById[budgetId])
                .ToList();
        }
    }

    public EntityState<Budget>? Get(Guid budgetId)
    {
        lock (store.SyncRoot)
        {
            return BudgetBelongsToCurrentUser(budgetId)
                && store.BudgetsById.TryGetValue(budgetId, out var budget)
                ? new InMemoryEntityState<Budget>(budget, store.BudgetVersionsById[budgetId])
                : null;
        }
    }

    public BudgetItem? GetBudgetItem(Guid budgetId, Guid budgetItemId)
    {
        lock (store.SyncRoot)
        {
            if (!BudgetBelongsToCurrentUser(budgetId)
                || !store.BudgetsById.TryGetValue(budgetId, out var budget))
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
            foreach (var budgetId in store.BudgetIdsByOwner.GetValueOrDefault(currentUser.UserId, []))
            {
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

    private bool HasBudgetNamedCore(NormalizedName name, Guid? exceptBudgetId)
    {
        return store.BudgetIdsByOwnerAndName.TryGetValue(
                new BudgetNameIndexKey(currentUser.UserId, name),
                out var budgetId)
            && budgetId != exceptBudgetId;
    }

    private BudgetSaveResult SaveCore(Budget budget, long? expectedVersion = null)
    {
        var hasExistingBudget = store.BudgetsById.TryGetValue(budget.BudgetId, out var existingBudget);

        if (expectedVersion.HasValue && !hasExistingBudget)
        {
            return new BudgetSaveResult.NotFound();
        }

        if (hasExistingBudget && !BudgetBelongsToCurrentUser(budget.BudgetId))
        {
            return expectedVersion.HasValue
                ? new BudgetSaveResult.NotFound()
                : new BudgetSaveResult.DuplicateIdentity();
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

        var nameIndexKey = new BudgetNameIndexKey(currentUser.UserId, budget.Name);

        if (store.BudgetIdsByOwnerAndName.TryGetValue(nameIndexKey, out var existingBudgetId)
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
            store.BudgetIdsByOwnerAndName.Remove(new BudgetNameIndexKey(currentUser.UserId, existingBudget!.Name));
        }
        else
        {
            store.BudgetOwnersById[budget.BudgetId] = currentUser.UserId;
            GetOrCreateBudgetIdsForCurrentUser().Add(budget.BudgetId);
        }

        store.BudgetsById[budget.BudgetId] = budget;
        store.BudgetVersionsById[budget.BudgetId] = hasExistingBudget
            ? store.BudgetVersionsById[budget.BudgetId] + 1
            : 1;
        store.BudgetIdsByOwnerAndName[nameIndexKey] = budget.BudgetId;

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

    private bool BudgetBelongsToCurrentUser(Guid budgetId)
    {
        return store.BudgetOwnersById.TryGetValue(budgetId, out var ownerId)
            && ownerId == currentUser.UserId;
    }

    private List<Guid> GetOrCreateBudgetIdsForCurrentUser()
    {
        if (!store.BudgetIdsByOwner.TryGetValue(currentUser.UserId, out var budgetIds))
        {
            budgetIds = [];
            store.BudgetIdsByOwner[currentUser.UserId] = budgetIds;
        }

        return budgetIds;
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
