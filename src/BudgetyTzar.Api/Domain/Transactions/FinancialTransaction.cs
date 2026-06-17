namespace BudgetyTzar.Api;

public enum TransactionDirection
{
    Debit,
    Credit
}

public enum TransactionAssignmentStatus
{
    Unassigned,
    PartiallyAssigned,
    FullyAssigned
}

public sealed class FinancialTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public Guid? ImportBatchId { get; set; }
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
        string? notes,
        Guid? importBatchId = null) =>
        new()
        {
            BudgetId = budgetId,
            ImportBatchId = importBatchId,
            TransactionDate = transactionDate,
            Description = description.Trim(),
            Amount = MoneyAmount.Positive(amount).Value,
            Direction = direction,
            SourceAccount = sourceAccount,
            ExternalReference = externalReference,
            Notes = notes
        };

    public void Edit(
        DateOnly transactionDate,
        string description,
        decimal amount,
        TransactionDirection direction,
        string? sourceAccount,
        string? externalReference,
        string? notes)
    {
        TransactionDate = transactionDate;
        Description = description.Trim();
        Amount = MoneyAmount.Positive(amount).Value;
        Direction = direction;
        SourceAccount = sourceAccount;
        ExternalReference = externalReference;
        Notes = notes;
    }

    public void Ignore() => IsIgnored = true;

    public IReadOnlyList<TransactionAssignment> ReplaceAssignments(IReadOnlyCollection<TransactionAssignmentItem> assignments)
    {
        var totalAssigned = assignments.Sum(x => x.Amount);
        if (totalAssigned > Amount)
        {
            throw new InvalidOperationException("Total assigned amount cannot exceed the transaction amount.");
        }

        return assignments
            .Select(x => TransactionAssignment.Create(Id, x.BudgetLineId, x.Amount))
            .ToList();
    }

    public TransactionAssignmentStatus GetAssignmentStatus(decimal assignedAmount)
    {
        if (assignedAmount == 0)
        {
            return TransactionAssignmentStatus.Unassigned;
        }

        return assignedAmount < Amount
            ? TransactionAssignmentStatus.PartiallyAssigned
            : TransactionAssignmentStatus.FullyAssigned;
    }
}
