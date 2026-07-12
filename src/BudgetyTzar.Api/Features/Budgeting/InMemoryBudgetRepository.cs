using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Identity;

namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class InMemoryBudgetRepository : IBudgetRepository
{
    private readonly InMemoryDataStore store;
    private readonly ApplicationUserId userId;

    public InMemoryBudgetRepository(InMemoryDataStore store, ICurrentUser currentUser)
    {
        this.store = store;
        userId = currentUser.UserId;
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
                .Where(BudgetBelongsToCurrentUser)
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
            foreach (var budget in GetAll())
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
        return store.BudgetIdsByName.TryGetValue(new UserBudgetNameKey(userId, name), out var budgetId)
            && budgetId != exceptBudgetId;
    }

    private BudgetSaveResult SaveCore(Budget budget, long? expectedVersion = null)
    {
        var hasExistingBudget = store.BudgetsById.TryGetValue(budget.BudgetId, out var existingBudget);
        var existingBudgetBelongsToCurrentUser = BudgetBelongsToCurrentUser(budget.BudgetId);

        if (expectedVersion.HasValue && !hasExistingBudget)
        {
            return new BudgetSaveResult.NotFound();
        }

        if (expectedVersion.HasValue && !existingBudgetBelongsToCurrentUser)
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

        if (store.BudgetIdsByName.TryGetValue(new UserBudgetNameKey(userId, budget.Name), out var existingBudgetId)
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
            store.BudgetIdsByName.Remove(new UserBudgetNameKey(userId, existingBudget!.Name));
        }
        else
        {
            store.BudgetIds.Add(budget.BudgetId);
            store.BudgetOwnersById[budget.BudgetId] = userId;
        }

        store.BudgetsById[budget.BudgetId] = budget;
        store.BudgetVersionsById[budget.BudgetId] = hasExistingBudget
            ? store.BudgetVersionsById[budget.BudgetId] + 1
            : 1;
        store.BudgetIdsByName[new UserBudgetNameKey(userId, budget.Name)] = budget.BudgetId;

        return new BudgetSaveResult.Saved(budget);
    }

    private bool RemovedBudgetItemHasAllocations(Budget existingBudget, Budget updatedBudget)
    {
        var removedBudgetItemIds = existingBudget.BudgetItems
            .Select(budgetItem => budgetItem.BudgetItemId)
            .Except(updatedBudget.BudgetItems.Select(budgetItem => budgetItem.BudgetItemId))
            .ToHashSet();

        return removedBudgetItemIds.Count > 0
            && store.AllocationsByTransactionId.Any(entry =>
                store.AllocationOwnersByTransactionId.GetValueOrDefault(entry.Key) == userId
                && removedBudgetItemIds.Contains(entry.Value.BudgetItemId));
    }

    private bool BudgetBelongsToCurrentUser(Guid budgetId)
    {
        return store.BudgetOwnersById.TryGetValue(budgetId, out var ownerId)
            && ownerId == userId;
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
