namespace BudgetyTzar.Api.Application.Budgeting;

internal static class BudgetItemValidationErrors
{
    public static Dictionary<string, string[]> ArchivedBudgetItemErrors() => new()
    {
        ["budgetItemId"] = ["Archived budget items can only be used for retrospective corrections dated on or before the archive date."]
    };
}
