using System.Text.Json.Serialization;
using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Api;

[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum TransactionDirection
{
    Debit,
    Credit
}

[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum TransactionAllocationStatus
{
    Unallocated,
    PartiallyAllocated,
    FullyAllocated
}

public sealed class FinancialTransaction
{
    public const string AmountBelowAllocatedTotalMessage = "Transaction amount cannot be less than the current allocated total.";
    public const string DuplicateBudgetItemAllocationMessage = "A budget item can only be allocated once per transaction.";
    public const string AllocationsExceedTransactionAmountMessage = "Total allocated amount cannot exceed the transaction amount.";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public DateOnly TransactionDate { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public TransactionDirection Direction { get; set; }
    public string? SourceAccount { get; set; }
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
    public bool IsIgnored { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static FinancialTransaction Create(
        Guid budgetId,
        DateOnly transactionDate,
        string description,
        decimal amount,
        TransactionDirection direction,
        string? sourceAccount,
        string? externalReference,
        string? notes) =>
        new()
        {
            BudgetId = budgetId,
            TransactionDate = transactionDate,
            Description = description.Trim(),
            Amount = MoneyAmount.Positive(amount).Value,
            Direction = direction,
            SourceAccount = sourceAccount,
            ExternalReference = externalReference,
            Notes = notes
        };

    public DomainEvent CreatedEvent() =>
        new(
            "TransactionManuallyCreated",
            BudgetId,
            nameof(FinancialTransaction),
            Id,
            $"Created transaction {Description} for {Amount} {Direction}.",
            Payload: new TransactionManuallyCreatedPayload(
                Id,
                BudgetId,
                TransactionDate,
                Description,
                Amount,
                Direction,
                SourceAccount,
                ExternalReference,
                Notes,
                IsIgnored));

    public string? ValidateEdit(decimal amount, decimal allocatedTotal) =>
        amount < allocatedTotal ? AmountBelowAllocatedTotalMessage : null;

    public DomainEvent Edit(
        DateOnly transactionDate,
        string description,
        decimal amount,
        TransactionDirection direction,
        string? sourceAccount,
        string? externalReference,
        string? notes,
        decimal allocatedTotal)
    {
        var validationError = ValidateEdit(amount, allocatedTotal);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        var previousDescription = Description;
        var previousAmount = Amount;
        var previousDirection = Direction;

        TransactionDate = transactionDate;
        Description = description.Trim();
        Amount = MoneyAmount.Positive(amount).Value;
        Direction = direction;
        SourceAccount = sourceAccount;
        ExternalReference = externalReference;
        Notes = notes;

        return new DomainEvent(
            "TransactionEdited",
            BudgetId,
            nameof(FinancialTransaction),
            Id,
            $"Edited transaction {Description}.",
            $"Previous={previousDescription}, {previousAmount} {previousDirection}; New={Description}, {Amount} {Direction}",
            Payload: new TransactionEditedPayload(
                Id,
                BudgetId,
                TransactionDate,
                Description,
                Amount,
                Direction,
                SourceAccount,
                ExternalReference,
                Notes,
                IsIgnored));
    }

    public DomainEvent Ignore()
    {
        IsIgnored = true;

        return new DomainEvent(
            "TransactionIgnored",
            BudgetId,
            nameof(FinancialTransaction),
            Id,
            $"Ignored transaction {Description}.",
            Payload: new TransactionIgnoredPayload(
                Id,
                BudgetId,
                TransactionDate,
                Description,
                Amount,
                Direction,
                SourceAccount,
                ExternalReference,
                Notes,
                IsIgnored));
    }

    public string? ValidateReplacementAllocations(IReadOnlyCollection<TransactionAllocationItem> allocations)
    {
        var requestedItemIds = allocations.Select(x => x.BudgetItemId).ToArray();
        if (requestedItemIds.Distinct().Count() != requestedItemIds.Length)
        {
            return DuplicateBudgetItemAllocationMessage;
        }

        var totalAllocated = allocations.Sum(x => x.Amount);
        return totalAllocated > Amount ? AllocationsExceedTransactionAmountMessage : null;
    }

    public IReadOnlyList<TransactionAllocation> ReplaceAllocations(IReadOnlyCollection<TransactionAllocationItem> allocations)
    {
        var validationError = ValidateReplacementAllocations(allocations);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        return allocations
            .Select(x => TransactionAllocation.Create(Id, x.BudgetItemId, x.Amount, x.Notes))
            .ToList();
    }

    public DomainEvent AllocationsReplacedEvent(
        IReadOnlyCollection<TransactionAllocation> existingAllocations,
        IReadOnlyCollection<TransactionAllocationItem> replacementAllocations) =>
        new(
            "TransactionAllocationsReplaced",
            BudgetId,
            nameof(FinancialTransaction),
            Id,
            $"Allocated transaction {Description}.",
            $"Previous={FormatAllocations(existingAllocations)}; New={FormatAllocations(replacementAllocations)}",
            Payload: new TransactionAllocationsReplacedPayload(
                Id,
                BudgetId,
                Amount,
                replacementAllocations
                    .Select(x => new TransactionAllocationPayload(
                        x.BudgetItemId,
                        x.Amount,
                        string.IsNullOrWhiteSpace(x.Notes) ? null : x.Notes.Trim()))
                    .ToList()));

    public DomainEvent AllocationsClearedEvent(IReadOnlyCollection<TransactionAllocation> existingAllocations) =>
        new(
            "TransactionAllocationsCleared",
            BudgetId,
            nameof(FinancialTransaction),
            Id,
            $"Cleared allocations for transaction {Description}.",
            FormatAllocations(existingAllocations),
            Payload: new TransactionAllocationsClearedPayload(
                Id,
                BudgetId,
                existingAllocations
                    .Select(x => new TransactionAllocationPayload(x.BudgetItemId, x.Amount, x.Notes))
                    .ToList()));

    public TransactionAllocationStatus GetAllocationStatus(decimal allocatedAmount)
    {
        if (allocatedAmount == 0)
        {
            return TransactionAllocationStatus.Unallocated;
        }

        return allocatedAmount < Amount
            ? TransactionAllocationStatus.PartiallyAllocated
            : TransactionAllocationStatus.FullyAllocated;
    }

    private static string FormatAllocations(IEnumerable<TransactionAllocation> allocations) =>
        string.Join("; ", allocations.Select(x => FormatAllocation(x.BudgetItemId, x.Amount, x.Notes)));

    private static string FormatAllocations(IEnumerable<TransactionAllocationItem> allocations) =>
        string.Join("; ", allocations.Select(x => FormatAllocation(x.BudgetItemId, x.Amount, x.Notes)));

    private static string FormatAllocation(Guid budgetItemId, decimal amount, string? notes) =>
        string.IsNullOrWhiteSpace(notes)
            ? $"{budgetItemId}:{amount}"
            : $"{budgetItemId}:{amount} ({notes.Trim()})";
}
