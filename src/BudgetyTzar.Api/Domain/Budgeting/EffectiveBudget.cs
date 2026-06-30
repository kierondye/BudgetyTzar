using System.Collections.ObjectModel;

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
    public const string PositiveAmountRequiredMessage = MoneyAmount.PositiveAmountRequiredMessage;
    public const string MoneyScaleExceededMessage = MoneyAmount.MoneyScaleExceededMessage;

    private readonly ReadOnlyDictionary<Guid, EffectiveBudgetItem> items;
    private readonly ReadOnlyCollection<BudgetAdjustment> pendingAdjustments;
    private readonly ReadOnlyCollection<DomainEvent> pendingEvents;

    internal EffectiveBudget(Guid budgetId, DateOnly date, decimal netPlannedAmount, IReadOnlyCollection<EffectiveBudgetItemState> items)
    {
        var effectiveBudgetItems = items.ToDictionary(
            x => ValidateItemBelongsToBudget(budgetId, x.BudgetItem),
            x => new EffectiveBudgetItem(x.BudgetItem, x.PlannedAmount));

        BudgetId = budgetId;
        Date = date;
        NetPlannedAmount = netPlannedAmount;
        this.items = new ReadOnlyDictionary<Guid, EffectiveBudgetItem>(effectiveBudgetItems);
        pendingAdjustments = Array.AsReadOnly(Array.Empty<BudgetAdjustment>());
        pendingEvents = Array.AsReadOnly(Array.Empty<DomainEvent>());
    }

    private EffectiveBudget(
        Guid budgetId,
        DateOnly date,
        decimal netPlannedAmount,
        IReadOnlyDictionary<Guid, EffectiveBudgetItem> items,
        IReadOnlyCollection<BudgetAdjustment> pendingAdjustments,
        IReadOnlyCollection<DomainEvent> pendingEvents)
    {
        BudgetId = budgetId;
        Date = date;
        NetPlannedAmount = netPlannedAmount;
        this.items = new ReadOnlyDictionary<Guid, EffectiveBudgetItem>(items.ToDictionary());
        this.pendingAdjustments = ToReadOnlyCollection(pendingAdjustments);
        this.pendingEvents = ToReadOnlyCollection(pendingEvents);
    }

    public Guid BudgetId { get; }
    public DateOnly Date { get; }
    public decimal NetPlannedAmount { get; }
    public IReadOnlyCollection<BudgetAdjustment> PendingAdjustments => pendingAdjustments;
    public IReadOnlyCollection<DomainEvent> PendingEvents => pendingEvents;

    public EffectiveBudgetResult RecordAdjustment(
        Guid budgetItemId,
        PositiveMoneyAmount amount,
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

        var positiveAmount = amount.Value;
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
            amount,
            type,
            Date,
            notes);

        var updatedItems = items.ToDictionary(
            x => x.Key,
            x => x.Key == budgetItemId
                ? new EffectiveBudgetItem(x.Value.BudgetItem, updatedItemPlannedAmount)
                : x.Value);
        var updatedPendingAdjustments = pendingAdjustments.Append(adjustment).ToArray();
        var updatedPendingEvents = pendingEvents.Append(adjustment.RecordedEvent(item.BudgetItem.Name)).ToArray();
        var updatedBudget = new EffectiveBudget(
            BudgetId,
            Date,
            NetPlannedAmount + signedPlannedAmount,
            updatedItems,
            updatedPendingAdjustments,
            updatedPendingEvents);

        return new EffectiveBudgetResult.Success(updatedBudget);
    }

    internal string? ValidateEffectivePlannedPosition(decimal signedPlannedAmount) =>
        NetPlannedAmount + signedPlannedAmount >= 0
            ? null
            : NetPlannedSpendingExceededMessage;

    private static Guid ValidateItemBelongsToBudget(Guid budgetId, BudgetItem budgetItem)
    {
        if (budgetItem.BudgetId != budgetId)
        {
            throw new InvalidOperationException("Effective budget items must belong to the effective budget.");
        }

        return budgetItem.Id;
    }

    private static ReadOnlyCollection<T> ToReadOnlyCollection<T>(IEnumerable<T> items) =>
        Array.AsReadOnly(items.ToArray());

    private sealed class EffectiveBudgetItem
    {
        internal EffectiveBudgetItem(BudgetItem budgetItem, decimal plannedAmount) =>
            (BudgetItem, PlannedAmount) = (budgetItem, plannedAmount);

        public BudgetItem BudgetItem { get; }
        public decimal PlannedAmount { get; }
    }
}
