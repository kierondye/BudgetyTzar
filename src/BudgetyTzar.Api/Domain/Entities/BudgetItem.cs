using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Domain.Entities;

public sealed class BudgetItem
{
    private BudgetItem(
        Guid budgetItemId,
        NormalizedName name,
        BudgetItemKind kind,
        PositiveMoneyAmount plannedAmount)
    {
        BudgetItemId = budgetItemId;
        Name = name;
        Kind = kind;
        PlannedAmount = plannedAmount;
    }

    public Guid BudgetItemId { get; }

    public NormalizedName Name { get; }

    public BudgetItemKind Kind { get; }

    public PositiveMoneyAmount PlannedAmount { get; }

    public static CreateBudgetItemEntityResult Create(
        Guid budgetItemId,
        NormalizedName name,
        BudgetItemKind kind,
        PositiveMoneyAmount plannedAmount)
    {
        if (budgetItemId == Guid.Empty)
        {
            return new CreateBudgetItemEntityResult.InvalidIdentity();
        }

        return new CreateBudgetItemEntityResult.Created(new BudgetItem(budgetItemId, name, kind, plannedAmount));
    }

    public BudgetItem Rename(NormalizedName name)
    {
        return new BudgetItem(BudgetItemId, name, Kind, PlannedAmount);
    }

    public BudgetItem ChangePlannedAmount(PositiveMoneyAmount plannedAmount)
    {
        return new BudgetItem(BudgetItemId, Name, Kind, plannedAmount);
    }
}

public abstract record CreateBudgetItemEntityResult
{
    public sealed record Created(BudgetItem BudgetItem) : CreateBudgetItemEntityResult;

    public sealed record InvalidIdentity : CreateBudgetItemEntityResult;
}
