using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Api;

public sealed record BudgetReallocationAdjustment(Guid BudgetItemId, decimal Amount, BudgetAdjustmentType Direction);

public sealed class BudgetReallocation
{
    public const string ConsumptionItemsOnlyMessage = "Budget reallocations can only move budget between consumption items.";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public Guid FromBudgetItemId { get; set; }
    public Guid ToBudgetItemId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public required string Reason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BudgetReallocation Create(Guid budgetId, DateOnly date, string? notes) =>
        new()
        {
            BudgetId = budgetId,
            Date = date,
            Amount = 0m,
            Reason = notes?.Trim() ?? string.Empty,
            Notes = notes?.Trim()
        };

    public static string? ValidateAdjustments(IReadOnlyCollection<BudgetReallocationAdjustment> adjustments)
    {
        if (adjustments.Count < 2)
        {
            return "A reallocation must contain at least two adjustments.";
        }

        var creditTotal = adjustments.Where(x => x.Direction == BudgetAdjustmentType.Credit).Sum(x => x.Amount);
        var debitTotal = adjustments.Where(x => x.Direction == BudgetAdjustmentType.Debit).Sum(x => x.Amount);
        return creditTotal == debitTotal
            ? null
            : "Reallocation credits must equal reallocation debits.";
    }

    public static string? ValidateBudgetItems(IReadOnlyCollection<BudgetItem> budgetItems) =>
        budgetItems.All(x => x.Kind == BudgetItemKind.Consumption)
            ? null
            : ConsumptionItemsOnlyMessage;

    public IReadOnlyList<BudgetAdjustment> CreateLinkedAdjustments(IReadOnlyCollection<BudgetReallocationAdjustment> adjustments)
    {
        var validationError = ValidateAdjustments(adjustments);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        return adjustments
            .Select(x => BudgetAdjustment.Create(BudgetId, x.BudgetItemId, x.Amount, x.Direction, Date, Notes, Id))
            .ToList();
    }

    public DomainEvent RecordedEvent(IReadOnlyList<BudgetReallocationAdjustment> adjustments) =>
        new(
            "BudgetReallocationRecorded",
            BudgetId,
            nameof(BudgetReallocation),
            Id,
            $"Recorded budget reallocation {Id}: {Reason}",
            Payload: new BudgetReallocationRecordedPayload(
                Id,
                BudgetId,
                Date,
                Notes,
                adjustments
                    .Select(x => new BudgetReallocationAdjustmentPayload(x.BudgetItemId, x.Amount, x.Direction))
                    .ToList()));
}
