using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Api;

public sealed record BudgetReallocationAdjustment(Guid BudgetItemId, decimal Amount, BudgetAdjustmentType Direction);

public abstract record CreateLinkedBudgetAdjustmentsResult
{
    private CreateLinkedBudgetAdjustmentsResult()
    {
    }

    public sealed record Success(IReadOnlyList<BudgetAdjustment> Adjustments) : CreateLinkedBudgetAdjustmentsResult;

    public sealed record ValidationFailed(string Error) : CreateLinkedBudgetAdjustmentsResult;
}

public sealed class BudgetReallocation
{
    public const string ConsumptionItemsOnlyMessage = "Budget reallocations can only move budget between consumption items.";

    private BudgetReallocation()
    {
    }

    private BudgetReallocation(
        Guid id,
        Guid budgetId,
        Guid fromBudgetItemId,
        Guid toBudgetItemId,
        DateOnly date,
        decimal amount,
        string reason,
        string? notes,
        DateTimeOffset createdAt)
    {
        Id = id;
        BudgetId = budgetId;
        FromBudgetItemId = fromBudgetItemId;
        ToBudgetItemId = toBudgetItemId;
        Date = date;
        Amount = amount;
        Reason = reason;
        Notes = notes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid BudgetId { get; private set; }
    public Guid FromBudgetItemId { get; private set; }
    public Guid ToBudgetItemId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public static BudgetReallocation Create(Guid budgetId, DateOnly date, string? notes)
    {
        var trimmedNotes = notes?.Trim();
        return new BudgetReallocation(
            Guid.NewGuid(),
            budgetId,
            Guid.Empty,
            Guid.Empty,
            date,
            0m,
            trimmedNotes ?? string.Empty,
            trimmedNotes,
            DateTimeOffset.UtcNow);
    }

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

    public CreateLinkedBudgetAdjustmentsResult CreateLinkedAdjustments(IReadOnlyCollection<BudgetReallocationAdjustment> adjustments)
    {
        var validationError = ValidateAdjustments(adjustments);
        if (validationError is not null)
        {
            return new CreateLinkedBudgetAdjustmentsResult.ValidationFailed(validationError);
        }

        var linkedAdjustments = adjustments
            .Select(x => BudgetAdjustment.Create(BudgetId, x.BudgetItemId, x.Amount, x.Direction, Date, Notes, Id))
            .ToList();

        return new CreateLinkedBudgetAdjustmentsResult.Success(linkedAdjustments);
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
