using BudgetyTzar.Api.Authentication;
using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class InMemoryBudgetRepository
{
    private readonly InMemoryDataStore store;
    private readonly ICurrentUser currentUser;

    public InMemoryBudgetRepository(InMemoryDataStore store, ICurrentUser currentUser)
    {
        this.store = store;
        this.currentUser = currentUser;
    }

    public BudgetSaveResult Save(Budget budget)
    {
        lock (store.SyncRoot)
        {
            return SaveCore(currentUser.UserId, budget);
        }
    }

    public BudgetSaveResult Save(EntityState<Budget> budgetState)
    {
        var userId = currentUser.UserId;

        if (budgetState is not InMemoryEntityState<Budget> inMemoryState
            || inMemoryState.UserId != userId)
        {
            return new BudgetSaveResult.InvalidState();
        }

        lock (store.SyncRoot)
        {
            return SaveCore(userId, inMemoryState.Value, inMemoryState.Version);
        }
    }

    public bool HasBudgetNamed(
        NormalizedName name,
        Guid? exceptBudgetId = null)
    {
        lock (store.SyncRoot)
        {
            return HasBudgetNamedCore(currentUser.UserId, name, exceptBudgetId);
        }
    }

    public IReadOnlyList<Budget> GetAll()
    {
        lock (store.SyncRoot)
        {
            var userId = currentUser.UserId;

            return store.BudgetIds
                .Where(budgetId => IsOwner(userId, budgetId))
                .Select(budgetId => store.BudgetsById[budgetId])
                .ToList();
        }
    }

    public EntityState<Budget>? Get(Guid budgetId)
    {
        lock (store.SyncRoot)
        {
            var userId = currentUser.UserId;

            return IsOwner(userId, budgetId)
                && store.BudgetsById.TryGetValue(budgetId, out var budget)
                ? new InMemoryEntityState<Budget>(userId, budget, store.BudgetVersionsById[budgetId])
                : null;
        }
    }

    public BudgetItem? GetBudgetItem(
        Guid budgetId,
        Guid budgetItemId)
    {
        lock (store.SyncRoot)
        {
            var userId = currentUser.UserId;

            if (!IsOwner(userId, budgetId)
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
            var userId = currentUser.UserId;

            foreach (var budgetId in store.BudgetIds.Where(id => IsOwner(userId, id)))
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

    private bool HasBudgetNamedCore(
        ApplicationUserId userId,
        NormalizedName name,
        Guid? exceptBudgetId)
    {
        return store.BudgetIdsByName.TryGetValue((userId, name), out var budgetId)
            && budgetId != exceptBudgetId;
    }

    private BudgetSaveResult SaveCore(
        ApplicationUserId userId,
        Budget budget,
        long? expectedVersion = null)
    {
        var hasExistingBudget = store.BudgetsById.TryGetValue(budget.BudgetId, out var existingBudget);

        if (expectedVersion.HasValue
            && (!hasExistingBudget || !IsOwner(userId, budget.BudgetId)))
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

        if (store.BudgetIdsByName.TryGetValue((userId, budget.Name), out var existingBudgetId)
            && existingBudgetId != budget.BudgetId)
        {
            return new BudgetSaveResult.DuplicateName();
        }

        if (hasExistingBudget && RemovedBudgetItemHasAllocations(userId, existingBudget!, budget))
        {
            return new BudgetSaveResult.BudgetItemHasAllocations();
        }

        if (hasExistingBudget)
        {
            store.BudgetIdsByName.Remove((userId, existingBudget!.Name));
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
        store.BudgetIdsByName[(userId, budget.Name)] = budget.BudgetId;

        return new BudgetSaveResult.Saved(budget);
    }

    private bool RemovedBudgetItemHasAllocations(
        ApplicationUserId userId,
        Budget existingBudget,
        Budget updatedBudget)
    {
        var removedBudgetItemIds = existingBudget.BudgetItems
            .Select(budgetItem => budgetItem.BudgetItemId)
            .Except(updatedBudget.BudgetItems.Select(budgetItem => budgetItem.BudgetItemId))
            .ToHashSet();

        return removedBudgetItemIds.Count > 0
            && store.AllocationsByTransactionId.Any(pair =>
                IsTransactionOwner(userId, pair.Key)
                && removedBudgetItemIds.Contains(pair.Value.BudgetItemId));
    }

    private bool IsOwner(ApplicationUserId userId, Guid budgetId)
    {
        return store.BudgetOwnersById.TryGetValue(budgetId, out var ownerId)
            && ownerId == userId;
    }

    private bool IsTransactionOwner(ApplicationUserId userId, Guid transactionId)
    {
        return store.TransactionOwnersById.TryGetValue(transactionId, out var ownerId)
            && ownerId == userId;
    }

    private sealed class InMemoryEntityState<T>(
        ApplicationUserId userId,
        T value,
        long version) : EntityState<T>(value)
    {
        public ApplicationUserId UserId { get; } = userId;

        public long Version { get; } = version;

        public override EntityState<T> Update(T value)
        {
            return new InMemoryEntityState<T>(UserId, value, Version);
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
