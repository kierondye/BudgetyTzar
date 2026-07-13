namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class TransactionRecord
{
    public Guid TransactionId { get; set; }

    public Guid ApplicationUserId { get; set; }

    public required string Description { get; set; }

    public required string Type { get; set; }

    public DateOnly TransactionDate { get; set; }

    public decimal Amount { get; set; }

    public required string Currency { get; set; }

    public int RecordedOrder { get; set; }
}
