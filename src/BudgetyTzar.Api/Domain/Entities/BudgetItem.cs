using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Domain.Entities;

public sealed class BudgetItem
{
    private BudgetItem(
        Guid budgetItemId,
        string name,
        BudgetItemKind kind,
        PositiveMoneyAmount plannedAmount)
    {
        BudgetItemId = budgetItemId;
        Name = name;
        Kind = kind;
        PlannedAmount = plannedAmount;
    }

    public Guid BudgetItemId { get; }

    public string Name { get; }

    public BudgetItemKind Kind { get; }

    public PositiveMoneyAmount PlannedAmount { get; }

    public static CreateBudgetItemEntityResult Create(
        Guid budgetItemId,
        string name,
        BudgetItemKind kind,
        PositiveMoneyAmount plannedAmount)
    {
        if (budgetItemId == Guid.Empty)
        {
            return new CreateBudgetItemEntityResult.InvalidIdentity();
        }

        if (!TryNormalizeName(name, out var normalizedName))
        {
            return new CreateBudgetItemEntityResult.InvalidName();
        }

        return new CreateBudgetItemEntityResult.Created(new BudgetItem(budgetItemId, normalizedName, kind, plannedAmount));
    }

    public RenameBudgetItemEntityResult Rename(string name)
    {
        if (!TryNormalizeName(name, out var normalizedName))
        {
            return new RenameBudgetItemEntityResult.InvalidName();
        }

        return new RenameBudgetItemEntityResult.Renamed(new BudgetItem(BudgetItemId, normalizedName, Kind, PlannedAmount));
    }

    public BudgetItem ChangePlannedAmount(PositiveMoneyAmount plannedAmount)
    {
        return new BudgetItem(BudgetItemId, Name, Kind, plannedAmount);
    }

    private static bool TryNormalizeName(string name, out string normalizedName)
    {
        normalizedName = string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        normalizedName = name.Trim();
        return true;
    }
}

public abstract record CreateBudgetItemEntityResult
{
    public sealed record Created(BudgetItem BudgetItem) : CreateBudgetItemEntityResult;

    public sealed record InvalidIdentity : CreateBudgetItemEntityResult;

    public sealed record InvalidName : CreateBudgetItemEntityResult;
}

public abstract record RenameBudgetItemEntityResult
{
    public sealed record Renamed(BudgetItem BudgetItem) : RenameBudgetItemEntityResult;

    public sealed record InvalidName : RenameBudgetItemEntityResult;
}
