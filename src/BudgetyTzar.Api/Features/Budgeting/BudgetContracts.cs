using BudgetyTzar.Api.Domain.Entities;

namespace BudgetyTzar.Api.Features.Budgeting;

public sealed record CreateBudgetRequest(string Name, string Currency);

public sealed record RenameBudgetRequest(string Name);

public sealed record CreateBudgetItemRequest(string Name, string Kind, string PlannedAmount);

public sealed record RenameBudgetItemRequest(string Name);

public sealed record ChangeBudgetItemPlannedAmountRequest(string PlannedAmount);

public sealed record BudgetResponse(Guid BudgetId, string Name, string Currency, IReadOnlyList<BudgetItemResponse> BudgetItems)
{
    public static BudgetResponse FromBudget(Budget budget)
    {
        var budgetItems = budget.BudgetItems
            .Select(BudgetItemResponse.FromBudgetItem)
            .ToList();

        return new BudgetResponse(budget.BudgetId, budget.Name.Value, budget.Currency.Value, budgetItems);
    }
}

public sealed record BudgetListItemResponse(Guid BudgetId, string Name, string Currency)
{
    public static BudgetListItemResponse FromBudget(Budget budget)
    {
        return new BudgetListItemResponse(budget.BudgetId, budget.Name.Value, budget.Currency.Value);
    }
}

public sealed record BudgetItemResponse(Guid BudgetItemId, string Name, string Kind, string PlannedAmount)
{
    public static BudgetItemResponse FromBudgetItem(BudgetItem budgetItem)
    {
        return new BudgetItemResponse(
            budgetItem.BudgetItemId,
            budgetItem.Name.Value,
            budgetItem.Kind.Value,
            budgetItem.PlannedAmount.FormattedValue);
    }
}
