namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class TransactionAllocationRecord
{
    public Guid TransactionId { get; set; }

    public Guid ApplicationUserId { get; set; }

    public Guid BudgetItemId { get; set; }

    public required string Currency { get; set; }
}
