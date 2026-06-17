namespace BudgetyTzar.Api;

public enum TransactionImportBatchStatus
{
    Previewed,
    Committed
}

public sealed class TransactionImportBatch
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public required string FileName { get; set; }
    public TransactionImportBatchStatus Status { get; set; } = TransactionImportBatchStatus.Previewed;
    public int RowCount { get; set; }
    public int DuplicateCandidateCount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CommittedAt { get; set; }

    public static TransactionImportBatch Preview(Guid budgetId, string fileName, int rowCount) =>
        new()
        {
            BudgetId = budgetId,
            FileName = fileName.Trim(),
            RowCount = rowCount
        };

    public void Commit(DateTimeOffset committedAt)
    {
        Status = TransactionImportBatchStatus.Committed;
        CommittedAt = committedAt;
    }
}
