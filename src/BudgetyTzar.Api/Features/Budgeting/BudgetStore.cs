namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class BudgetStore
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, Budget> budgetsById = [];
    private readonly List<Guid> budgetIds = [];

    public Budget Create(string name, CurrencyCode currency)
    {
        var budget = new Budget(Guid.NewGuid(), name, currency, []);

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

    public Budget? Rename(Guid budgetId, string name)
    {
        lock (syncRoot)
        {
            if (!budgetsById.TryGetValue(budgetId, out var budget))
            {
                return null;
            }

            var renamedBudget = budget with { Name = name };
            budgetsById[budgetId] = renamedBudget;

            return renamedBudget;
        }
    }

    public BudgetItem? AddBudgetItem(Guid budgetId, string name, BudgetItemKind kind, PlannedAmount plannedAmount)
    {
        lock (syncRoot)
        {
            if (!budgetsById.TryGetValue(budgetId, out var budget))
            {
                return null;
            }

            var budgetItem = new BudgetItem(Guid.NewGuid(), name, kind, plannedAmount);
            var budgetItems = budget.BudgetItems.Append(budgetItem).ToList();
            budgetsById[budgetId] = budget with { BudgetItems = budgetItems };

            return budgetItem;
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
}

public sealed record Budget(Guid BudgetId, string Name, CurrencyCode Currency, IReadOnlyList<BudgetItem> BudgetItems);

public sealed record BudgetItem(Guid BudgetItemId, string Name, BudgetItemKind Kind, PlannedAmount PlannedAmount);
