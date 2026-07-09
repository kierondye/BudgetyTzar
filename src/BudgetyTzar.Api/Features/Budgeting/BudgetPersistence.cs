using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Features.Budgeting;

public interface IBudgetRepository
{
    BudgetSaveResult Save(Budget budget);

    BudgetSaveResult Save(EntityState<Budget> budgetState);

    bool HasBudgetNamed(NormalizedName name, Guid? exceptBudgetId = null);

    IReadOnlyList<Budget> GetAll();

    EntityState<Budget>? Get(Guid budgetId);

    BudgetItem? GetBudgetItem(Guid budgetId, Guid budgetItemId);

    BudgetItemReference? GetBudgetItemReference(Guid budgetItemId);
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
