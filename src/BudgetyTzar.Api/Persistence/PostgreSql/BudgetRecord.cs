namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class BudgetRecord
{
    public Guid BudgetId { get; set; }

    public Guid ApplicationUserId { get; set; }

    public required string Name { get; set; }

    public required string Currency { get; set; }

    public long Version { get; set; }
}
