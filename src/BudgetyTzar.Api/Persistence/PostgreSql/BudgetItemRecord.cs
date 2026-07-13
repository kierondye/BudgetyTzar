namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class BudgetItemRecord
{
    public Guid BudgetItemId { get; set; }

    public Guid BudgetId { get; set; }

    public required string Name { get; set; }

    public required string Kind { get; set; }

    public decimal PlannedAmount { get; set; }

    public int CreatedOrder { get; set; }
}
