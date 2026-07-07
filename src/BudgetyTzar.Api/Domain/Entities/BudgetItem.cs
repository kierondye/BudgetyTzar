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

    public static BudgetItem Create(
        Guid budgetItemId,
        string name,
        BudgetItemKind kind,
        PositiveMoneyAmount plannedAmount)
    {
        if (budgetItemId == Guid.Empty)
        {
            throw new ArgumentException("Identity must not be empty.", nameof(budgetItemId));
        }

        var normalizedName = NormalizeName(name);

        return new BudgetItem(budgetItemId, normalizedName, kind, plannedAmount);
    }

    public BudgetItem Rename(string name)
    {
        var normalizedName = NormalizeName(name);

        return new BudgetItem(BudgetItemId, normalizedName, Kind, PlannedAmount);
    }

    public BudgetItem ChangePlannedAmount(PositiveMoneyAmount plannedAmount)
    {
        return new BudgetItem(BudgetItemId, Name, Kind, plannedAmount);
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Budget item name is required.", nameof(name));
        }

        return name.Trim();
    }
}
