namespace BudgetyTzar.Api;

public sealed class BudgetPeriod
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateRange DateRange => new(StartDate, EndDate);

    public static BudgetPeriod Create(Guid budgetId, string name, DateOnly startDate, DateOnly endDate) =>
        new()
        {
            BudgetId = budgetId,
            Name = name.Trim(),
            StartDate = startDate,
            EndDate = endDate
        };

    public IReadOnlyList<BudgetLineAllocation> ReplaceAllocations(IReadOnlyCollection<BudgetLineAllocationItem> allocations) =>
        allocations
            .Select(x => BudgetLineAllocation.Create(Id, x.BudgetLineId, x.Amount))
            .ToList();

    public BudgetAdjustment RecordAdjustment(Guid budgetLineId, decimal amount, string reason) =>
        BudgetAdjustment.Create(Id, budgetLineId, amount, reason);

    public BudgetAdjustment RecordAdjustment(Guid budgetLineId, decimal amount, string reason, DateOnly date) =>
        BudgetAdjustment.CreateLegacy(BudgetId, Id, budgetLineId, date, amount, reason);

    public BudgetReallocation RecordReallocation(Guid fromBudgetLineId, Guid toBudgetLineId, decimal amount, string reason) =>
        BudgetReallocation.Create(Id, fromBudgetLineId, toBudgetLineId, amount, reason);

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetPeriodCreated",
            BudgetId,
            Id,
            nameof(BudgetPeriod),
            Id,
            $"Created period {Name}.");
}
