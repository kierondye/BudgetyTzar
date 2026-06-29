using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Api;

public sealed class Budget
{
    public const string NetPlannedSpendingExceededMessage = "Net planned spending must not exceed net planned income.";
    public const string ConsumptionBudgetItemBecameFundingMessage = "A consumption item must not become a funding source through budget adjustments.";
    public const string FundingBudgetItemBecameConsumptionMessage = "A funding item must not become a consumption item through budget adjustments.";
    public const string DuplicateBudgetItemNameMessage = "A budget item with this name already exists in this budget.";

    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static Budget Create(string name, string currency) =>
        new()
        {
            Name = name.Trim(),
            Currency = new Currency(currency).Value
        };

    public bool CanRecordAdjustment(IReadOnlyCollection<BudgetAdjustment> existingAdjustments, BudgetAdjustment pendingAdjustment)
    {
        var netPlannedAmount = existingAdjustments
            .Where(x => x.Date <= pendingAdjustment.Date)
            .Sum(x => x.SignedPlannedAmount());

        return netPlannedAmount + pendingAdjustment.SignedPlannedAmount() >= 0;
    }

    public string? ValidateBudgetItemKindForAdjustment(
        BudgetItem budgetItem,
        IReadOnlyCollection<BudgetAdjustment> existingAdjustments,
        BudgetAdjustment pendingAdjustment)
    {
        var itemPlannedAmount = existingAdjustments
            .Where(x => x.BudgetItemId == budgetItem.Id && x.Date <= pendingAdjustment.Date)
            .Sum(x => x.SignedPlannedAmount());
        var pendingItemPlannedAmount = itemPlannedAmount + pendingAdjustment.SignedPlannedAmount();

        return budgetItem.Kind switch
        {
            BudgetItemKind.Consumption when pendingItemPlannedAmount > 0 => ConsumptionBudgetItemBecameFundingMessage,
            BudgetItemKind.Funding when pendingItemPlannedAmount < 0 => FundingBudgetItemBecameConsumptionMessage,
            _ => null
        };
    }

    public string? ValidateBudgetItemName(IReadOnlyCollection<BudgetItem> existingItems, string name)
    {
        var trimmedName = name.Trim();
        return existingItems.Any(x => x.Name == trimmedName) ? DuplicateBudgetItemNameMessage : null;
    }

    public BudgetItem CreateBudgetItem(IReadOnlyCollection<BudgetItem> existingItems, string name, BudgetItemKind kind)
    {
        var validationError = ValidateBudgetItemName(existingItems, name);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        return BudgetItem.Create(Id, name, kind);
    }

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetCreated",
            Id,
            nameof(Budget),
            Id,
            $"Created budget {Name}.",
            Payload: new BudgetCreatedPayload(Id, Name, Currency));
}
