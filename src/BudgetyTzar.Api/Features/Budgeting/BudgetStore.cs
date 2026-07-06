namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class BudgetStore
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, Budget> budgetsById = [];
    private readonly List<Guid> budgetIds = [];

    public Budget Create(string name, string currency)
    {
        var budget = new Budget(Guid.NewGuid(), name, currency);

        lock (syncRoot)
        {
            budgetsById[budget.BudgetId] = budget;
            budgetIds.Add(budget.BudgetId);
        }

        return budget;
    }

    public IReadOnlyList<Budget> GetAll()
    {
        lock (syncRoot)
        {
            return budgetIds
                .Select(budgetId => budgetsById[budgetId])
                .ToList();
        }
    }

    public Budget? Get(Guid budgetId)
    {
        lock (syncRoot)
        {
            return budgetsById.GetValueOrDefault(budgetId);
        }
    }
}

public sealed record Budget(Guid BudgetId, string Name, string Currency);
