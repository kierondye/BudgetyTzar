using System.Collections.ObjectModel;

namespace BudgetyTzar.Api;

internal sealed record EffectiveBudgetItemState(BudgetItem BudgetItem, decimal PlannedAmount);

public sealed class EffectiveBudgetReallocationAdjustment
{
    public EffectiveBudgetReallocationAdjustment(
        Guid budgetItemId,
        PositiveMoneyAmount amount,
        BudgetAdjustmentType direction) =>
        (BudgetItemId, Amount, Direction) = (budgetItemId, amount, direction);

    public Guid BudgetItemId { get; }
    public PositiveMoneyAmount Amount { get; }
    public BudgetAdjustmentType Direction { get; }
}

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
    private readonly ReadOnlyCollection<BudgetReallocation> pendingReallocations;
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
        pendingReallocations = Array.AsReadOnly(Array.Empty<BudgetReallocation>());
        pendingEvents = Array.AsReadOnly(Array.Empty<DomainEvent>());
    }

    private EffectiveBudget(
        Guid budgetId,
        DateOnly date,
        decimal netPlannedAmount,
        IReadOnlyDictionary<Guid, EffectiveBudgetItem> items,
        IReadOnlyCollection<BudgetAdjustment> pendingAdjustments,
        IReadOnlyCollection<BudgetReallocation> pendingReallocations,
        IReadOnlyCollection<DomainEvent> pendingEvents)
    {
        BudgetId = budgetId;
        Date = date;
        NetPlannedAmount = netPlannedAmount;
        this.items = new ReadOnlyDictionary<Guid, EffectiveBudgetItem>(items.ToDictionary());
        this.pendingAdjustments = ToReadOnlyCollection(pendingAdjustments);
        this.pendingReallocations = ToReadOnlyCollection(pendingReallocations);
        this.pendingEvents = ToReadOnlyCollection(pendingEvents);
    }

    public Guid BudgetId { get; }
    public DateOnly Date { get; }
    public decimal NetPlannedAmount { get; }
    public IReadOnlyCollection<BudgetAdjustment> PendingAdjustments => pendingAdjustments;
    public IReadOnlyCollection<BudgetReallocation> PendingReallocations => pendingReallocations;
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
            pendingReallocations,
            updatedPendingEvents);

        return new EffectiveBudgetResult.Success(updatedBudget);
    }

    public EffectiveBudgetResult RecordReallocation(
        IReadOnlyCollection<EffectiveBudgetReallocationAdjustment> adjustments,
        string? notes)
    {
        var reallocationAdjustments = adjustments
            .Select(x => new BudgetReallocationAdjustment(x.BudgetItemId, x.Amount.Value, x.Direction))
            .ToArray();
        var validationError = BudgetReallocation.ValidateAdjustments(reallocationAdjustments);
        if (validationError is not null)
        {
            return new EffectiveBudgetResult.ValidationFailed(validationError);
        }

        var affectedItemIds = adjustments.Select(x => x.BudgetItemId).Distinct().ToArray();
        foreach (var budgetItemId in affectedItemIds)
        {
            if (!items.TryGetValue(budgetItemId, out var item))
            {
                return new EffectiveBudgetResult.ItemNotFound(budgetItemId);
            }

            if (!item.BudgetItem.CanAcceptActivityOn(Date))
            {
                return new EffectiveBudgetResult.ItemArchived(budgetItemId);
            }

            if (item.BudgetItem.Kind != BudgetItemKind.Consumption)
            {
                return new EffectiveBudgetResult.ValidationFailed(BudgetReallocation.ConsumptionItemsOnlyMessage);
            }
        }

        var reallocation = BudgetReallocation.Create(BudgetId, Date, notes);
        var linkedAdjustmentsResult = reallocation.CreateLinkedAdjustments(reallocationAdjustments);
        if (linkedAdjustmentsResult is CreateLinkedBudgetAdjustmentsResult.ValidationFailed linkedAdjustmentsValidationFailed)
        {
            return new EffectiveBudgetResult.ValidationFailed(linkedAdjustmentsValidationFailed.Error);
        }

        if (linkedAdjustmentsResult is not CreateLinkedBudgetAdjustmentsResult.Success linkedAdjustmentsCreated)
        {
            throw new InvalidOperationException("Unexpected linked budget adjustments result.");
        }

        var plannedChanges = reallocationAdjustments
            .GroupBy(x => x.BudgetItemId)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(y => y.Direction == BudgetAdjustmentType.Credit ? y.Amount : -y.Amount));
        var updatedItems = items.ToDictionary(
            x => x.Key,
            x => plannedChanges.TryGetValue(x.Key, out var plannedChange)
                ? new EffectiveBudgetItem(x.Value.BudgetItem, x.Value.PlannedAmount + plannedChange)
                : x.Value);
        var updatedPendingAdjustments = pendingAdjustments.Concat(linkedAdjustmentsCreated.Adjustments).ToArray();
        var updatedPendingReallocations = pendingReallocations.Append(reallocation).ToArray();
        var updatedPendingEvents = pendingEvents.Append(reallocation.RecordedEvent(reallocationAdjustments)).ToArray();

        var updatedBudget = new EffectiveBudget(
            BudgetId,
            Date,
            NetPlannedAmount,
            updatedItems,
            updatedPendingAdjustments,
            updatedPendingReallocations,
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
