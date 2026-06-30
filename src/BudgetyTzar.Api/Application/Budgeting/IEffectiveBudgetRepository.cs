namespace BudgetyTzar.Api;

public interface IEffectiveBudgetRepository
{
    Task<EffectiveBudgetSaveResult> Save(EffectiveBudget budget, CancellationToken ct);
}

public sealed record EffectiveBudgetSaveResult(BudgetAdjustment CreatedAdjustment, Guid? EventId);
