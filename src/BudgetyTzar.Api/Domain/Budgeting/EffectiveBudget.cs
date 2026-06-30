namespace BudgetyTzar.Api;

internal sealed record EffectiveBudgetItemState(BudgetItem BudgetItem, decimal PlannedAmount);

public abstract record EffectiveBudgetResult
{
    private EffectiveBudgetResult()
    {
    }

    public sealed record Success(EffectiveBudget Budget) : EffectiveBudgetResult;

    public sealed record ItemNotFound(Guid BudgetItemId) : EffectiveBudgetResult;

    public sealed record ItemArchived(Guid BudgetItemId) : EffectiveBudgetResult;

    public sealed record ValidationFailed(string Error) : EffectiveBudgetResult;
}

public sealed class EffectiveBudget
{
    public const string NetPlannedSpendingExceededMessage = "Net planned spending must not exceed net planned income.";
    public const string PositiveAmountRequiredMessage = "Amount must be greater than zero.";
    public const string MoneyScaleExceededMessage = "Money values must use at most two decimal places.";

    private readonly Dictionary<Guid, EffectiveBudgetItem> items;
    private readonly List<BudgetAdjustment> pendingAdjustments = [];
    private readonly List<DomainEvent> pendingEvents = [];

    internal EffectiveBudget(Guid budgetId, DateOnly date, decimal netPlannedAmount, IReadOnlyCollection<EffectiveBudgetItemState> items)
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
    public decimal NetPlannedAmount { get; private set; }
    public IReadOnlyCollection<BudgetAdjustment> PendingAdjustments => pendingAdjustments;
    public IReadOnlyCollection<DomainEvent> PendingEvents => pendingEvents;

    public EffectiveBudgetResult RecordAdjustment(
        Guid budgetItemId,
        decimal amount,
        BudgetAdjustmentType type,
        string? notes)
    {
        if (!items.TryGetValue(budgetItemId, out var item))
        {
            return new EffectiveBudgetResult.ItemNotFound(budgetItemId);
        }

        if (!item.BudgetItem.CanAcceptActivityOn(Date))
        {
            return new EffectiveBudgetResult.ItemArchived(budgetItemId);
        }

        var amountValidationError = ValidatePositiveMoney(amount);
        if (amountValidationError is not null)
        {
            return new EffectiveBudgetResult.ValidationFailed(amountValidationError);
        }

        var positiveAmount = MoneyAmount.Positive(amount).Value;
        var signedPlannedAmount = type == BudgetAdjustmentType.Credit ? positiveAmount : -positiveAmount;
        var budgetValidationError = ValidateEffectivePlannedPosition(signedPlannedAmount);
        if (budgetValidationError is not null)
        {
            return new EffectiveBudgetResult.ValidationFailed(budgetValidationError);
        }

        var updatedItemPlannedAmount = item.PlannedAmount + signedPlannedAmount;
        var itemValidationError = item.BudgetItem.ValidateEffectivePlannedPosition(updatedItemPlannedAmount);
        if (itemValidationError is not null)
        {
            return new EffectiveBudgetResult.ValidationFailed(itemValidationError);
        }

        var adjustment = BudgetAdjustment.Create(
            BudgetId,
            item.BudgetItem.Id,
            positiveAmount,
            type,
            Date,
            notes);

        item.PlannedAmount = updatedItemPlannedAmount;
        NetPlannedAmount += signedPlannedAmount;
        pendingAdjustments.Add(adjustment);
        pendingEvents.Add(adjustment.RecordedEvent(item.BudgetItem.Name));

        return new EffectiveBudgetResult.Success(this);
    }

    internal string? ValidateEffectivePlannedPosition(decimal signedPlannedAmount) =>
        NetPlannedAmount + signedPlannedAmount >= 0
            ? null
            : NetPlannedSpendingExceededMessage;

    private static string? ValidatePositiveMoney(decimal amount)
    {
        if (amount <= 0)
        {
            return PositiveAmountRequiredMessage;
        }

        return decimal.Round(amount, 2) == amount ? null : MoneyScaleExceededMessage;
    }

    private sealed class EffectiveBudgetItem
    {
        internal EffectiveBudgetItem(EffectiveBudget budget, BudgetItem budgetItem, decimal plannedAmount)
        {
            if (budget.BudgetId != budgetItem.BudgetId)
            {
                throw new InvalidOperationException("Effective budget items must belong to the effective budget.");
            }

            BudgetItem = budgetItem;
            PlannedAmount = plannedAmount;
        }

        public BudgetItem BudgetItem { get; }
        public decimal PlannedAmount { get; set; }
    }
}
