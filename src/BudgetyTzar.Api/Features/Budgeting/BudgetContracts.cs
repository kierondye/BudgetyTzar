namespace BudgetyTzar.Api.Features.Budgeting;

public sealed record CreateBudgetRequest(string Name, string Currency);

public sealed record BudgetResponse(Guid BudgetId, string Name, string Currency, IReadOnlyList<BudgetItemResponse> BudgetItems)
{
    public static BudgetResponse FromBudget(Budget budget)
    {
        return new BudgetResponse(budget.BudgetId, budget.Name, budget.Currency, []);
    }
}

public sealed record BudgetListItemResponse(Guid BudgetId, string Name, string Currency)
{
    public static BudgetListItemResponse FromBudget(Budget budget)
    {
        return new BudgetListItemResponse(budget.BudgetId, budget.Name, budget.Currency);
    }
}

public sealed record BudgetItemResponse(Guid BudgetItemId, string Name, string Kind, string PlannedAmount);
