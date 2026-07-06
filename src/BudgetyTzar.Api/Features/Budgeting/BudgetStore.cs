using System.Collections.Immutable;
using BudgetyTzar.Api.Features.Common;

namespace BudgetyTzar.Api.Features.Budgeting;

public sealed class BudgetStore
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, Budget> budgetsById = [];
    private readonly List<Guid> budgetIds = [];

    public CreateBudgetResult Create(string name, CurrencyCode currency)
    {
        var budget = new Budget(Guid.NewGuid(), name, currency, []);

        lock (syncRoot)
        {
            if (budgetsById.Values.Any(existingBudget => existingBudget.Name == name))
            {
                return new CreateBudgetResult.DuplicateName();
            }

            budgetsById[budget.BudgetId] = budget;
            budgetIds.Add(budget.BudgetId);
        }

        return new CreateBudgetResult.Created(budget);
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

    public RenameBudgetResult Rename(Guid budgetId, string name)
    {
        lock (syncRoot)
        {
            if (!budgetsById.TryGetValue(budgetId, out var budget))
            {
                return new RenameBudgetResult.NotFound();
            }

            if (budgetsById.Values.Any(existingBudget => existingBudget.BudgetId != budgetId && existingBudget.Name == name))
            {
                return new RenameBudgetResult.DuplicateName();
            }

            var renamedBudget = budget with { Name = name };
            budgetsById[budgetId] = renamedBudget;

            return new RenameBudgetResult.Renamed(renamedBudget);
        }
    }

    public AddBudgetItemResult AddBudgetItem(Guid budgetId, string name, BudgetItemKind kind, PositiveMoneyAmount plannedAmount)
    {
        lock (syncRoot)
        {
            if (!budgetsById.TryGetValue(budgetId, out var budget))
            {
                return new AddBudgetItemResult.NotFound();
            }

            if (budget.BudgetItems.Any(budgetItem => budgetItem.Name == name))
            {
                return new AddBudgetItemResult.DuplicateName();
            }

            var budgetItem = new BudgetItem(Guid.NewGuid(), name, kind, plannedAmount);
            var budgetItems = budget.BudgetItems.Add(budgetItem);
            budgetsById[budgetId] = budget with { BudgetItems = budgetItems };

            return new AddBudgetItemResult.Added(budgetItem);
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

public sealed record Budget(Guid BudgetId, string Name, CurrencyCode Currency, ImmutableArray<BudgetItem> BudgetItems);

public sealed record BudgetItem(Guid BudgetItemId, string Name, BudgetItemKind Kind, PositiveMoneyAmount PlannedAmount);

public abstract record CreateBudgetResult
{
    public sealed record Created(Budget Budget) : CreateBudgetResult;

    public sealed record DuplicateName : CreateBudgetResult;
}

public abstract record RenameBudgetResult
{
    public sealed record Renamed(Budget Budget) : RenameBudgetResult;

    public sealed record NotFound : RenameBudgetResult;

    public sealed record DuplicateName : RenameBudgetResult;
}

public abstract record AddBudgetItemResult
{
    public sealed record Added(BudgetItem BudgetItem) : AddBudgetItemResult;

    public sealed record NotFound : AddBudgetItemResult;

    public sealed record DuplicateName : AddBudgetItemResult;
}
