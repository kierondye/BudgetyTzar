namespace BudgetyTzar.Api;

public sealed record EffectiveBudgetItemState(BudgetItem BudgetItem, decimal PlannedAmount);

public abstract record EffectiveBudgetItemLookupResult;

public sealed record EffectiveBudgetItemFound(EffectiveBudgetItem Item) : EffectiveBudgetItemLookupResult;

public sealed record EffectiveBudgetItemNotFound : EffectiveBudgetItemLookupResult;

public abstract record EffectiveBudgetAdjustmentResult;

public sealed record EffectiveBudgetAdjustmentRecorded(BudgetAdjustment Adjustment, string BudgetItemName) : EffectiveBudgetAdjustmentResult;

public sealed record EffectiveBudgetAdjustmentArchivedBudgetItem : EffectiveBudgetAdjustmentResult;

public sealed record EffectiveBudgetAdjustmentValidationProblem(string Error) : EffectiveBudgetAdjustmentResult;

public sealed class EffectiveBudgetItem
{
    private readonly EffectiveBudget budget;

    internal EffectiveBudgetItem(EffectiveBudget budget, BudgetItem budgetItem, decimal plannedAmount)
    {
        this.budget = budget;
        BudgetItem = budgetItem;
        PlannedAmount = plannedAmount;
    }

    public BudgetItem BudgetItem { get; }
    public decimal PlannedAmount { get; }

    public EffectiveBudgetAdjustmentResult CreateAdjustment(decimal amount, BudgetAdjustmentType type, string? notes)
    {
        if (!BudgetItem.CanAcceptActivityOn(budget.Date))
        {
            return new EffectiveBudgetAdjustmentArchivedBudgetItem();
        }

        var positiveAmount = MoneyAmount.Positive(amount).Value;
        var signedPlannedAmount = type == BudgetAdjustmentType.Credit ? positiveAmount : -positiveAmount;
        var budgetValidationError = budget.ValidateEffectivePlannedPosition(signedPlannedAmount);
        if (budgetValidationError is not null)
        {
            return new EffectiveBudgetAdjustmentValidationProblem(budgetValidationError);
        }

        var itemValidationError = BudgetItem.ValidateEffectivePlannedPosition(PlannedAmount + signedPlannedAmount);
        if (itemValidationError is not null)
        {
            return new EffectiveBudgetAdjustmentValidationProblem(itemValidationError);
        }

        var adjustment = BudgetAdjustment.Create(
            budget.BudgetId,
            BudgetItem.Id,
            positiveAmount,
            type,
            budget.Date,
            notes);
        return new EffectiveBudgetAdjustmentRecorded(adjustment, BudgetItem.Name);
    }
}

public sealed class EffectiveBudget
{
    public const string NetPlannedSpendingExceededMessage = "Net planned spending must not exceed net planned income.";

    private readonly IReadOnlyDictionary<Guid, EffectiveBudgetItem> items;

    public EffectiveBudget(Guid budgetId, DateOnly date, decimal netPlannedAmount, IReadOnlyCollection<EffectiveBudgetItemState> items)
    {
        BudgetId = budgetId;
        Date = date;
        NetPlannedAmount = netPlannedAmount;
        this.items = items.ToDictionary(
            x =>
            {
                if (x.BudgetItem.BudgetId != budgetId)
                {
                    throw new InvalidOperationException("Effective budget items must belong to the effective budget.");
                }

                return x.BudgetItem.Id;
            },
            x => new EffectiveBudgetItem(this, x.BudgetItem, x.PlannedAmount));
    }

    public Guid BudgetId { get; }
    public DateOnly Date { get; }
    public decimal NetPlannedAmount { get; }

    public EffectiveBudgetItemLookupResult GetBudgetItem(Guid budgetItemId)
    {
        if (!items.TryGetValue(budgetItemId, out var item))
        {
            return new EffectiveBudgetItemNotFound();
        }

        return new EffectiveBudgetItemFound(item);
    }

    internal string? ValidateEffectivePlannedPosition(decimal signedPlannedAmount) =>
        NetPlannedAmount + signedPlannedAmount >= 0
            ? null
            : NetPlannedSpendingExceededMessage;
}
