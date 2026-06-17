namespace BudgetyTzar.Api;

public sealed class TransactionImportRow
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ImportBatchId { get; set; }
    public int RowNumber { get; set; }
    public DateOnly TransactionDate { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public TransactionDirection Direction { get; set; }
    public string? SourceAccount { get; set; }
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
    public bool IsDuplicateCandidate { get; set; }
    public string? DuplicateReason { get; set; }
    public bool IsCommitted { get; set; }
    public Guid? TransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public FinancialTransaction ToTransaction(Guid budgetId, Guid importBatchId) =>
        FinancialTransaction.Create(
            budgetId,
            TransactionDate,
            Description,
            Amount,
            Direction,
            SourceAccount,
            ExternalReference,
            Notes,
            importBatchId);

    public void MarkCommitted(Guid transactionId)
    {
        IsCommitted = true;
        TransactionId = transactionId;
    }
}
