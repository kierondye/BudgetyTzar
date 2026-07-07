namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class InMemoryBudgetRepository
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, Budget> budgetsById = [];
    private readonly List<Guid> budgetIds = [];

    public void Add(Budget budget)
    {
        lock (syncRoot)
        {
            budgetsById[budget.BudgetId] = budget;
            budgetIds.Add(budget.BudgetId);
        }
    }

    public void Save(Budget budget)
    {
        lock (syncRoot)
        {
            budgetsById[budget.BudgetId] = budget;
        }
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

    public bool HasBudgetNamed(string name, Guid? exceptBudgetId = null)
    {
        lock (syncRoot)
        {
            return budgetsById.Values.Any(budget =>
                budget.BudgetId != exceptBudgetId
                && string.Equals(budget.Name, name, StringComparison.Ordinal));
        }
    }

    public BudgetItem? GetBudgetItem(Guid budgetId, Guid budgetItemId)
    {
        lock (syncRoot)
        {
            return budgetsById.TryGetValue(budgetId, out var budget)
                ? budget.BudgetItems.SingleOrDefault(budgetItem => budgetItem.BudgetItemId == budgetItemId)
                : null;
        }
    }

    public BudgetItemReference? GetBudgetItemReference(Guid budgetItemId)
    {
        lock (syncRoot)
        {
            foreach (var budget in budgetsById.Values)
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
}
