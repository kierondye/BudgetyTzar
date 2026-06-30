namespace BudgetyTzar.Api;

public interface IBudgetRepository
{
    Task<BudgetLoadResult> GetBudgetWithItems(Guid budgetId, CancellationToken ct);
}

public abstract record BudgetLoadResult
{
    private BudgetLoadResult()
    {
    }

    public sealed record Success(Budget Budget) : BudgetLoadResult;

    public sealed record BudgetNotFound : BudgetLoadResult;
}
