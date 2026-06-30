namespace BudgetyTzar.Api;

public interface IEffectiveBudgetRepository
{
    Task<EffectiveBudgetLoadResult> GetEffectiveBudget(Guid budgetId, DateOnly date, CancellationToken ct);

    Task<EffectiveBudgetSaveResult> Save(EffectiveBudget budget, CancellationToken ct);
}

public abstract record EffectiveBudgetLoadResult
{
    private EffectiveBudgetLoadResult()
    {
    }

    public sealed record Success(EffectiveBudget Budget) : EffectiveBudgetLoadResult;

    public sealed record BudgetNotFound : EffectiveBudgetLoadResult;
}

public sealed record EffectiveBudgetSaveResult(BudgetAdjustment CreatedAdjustment, Guid? EventId);
