using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Api;

public sealed class Budget
{
    public const string NetPlannedSpendingExceededMessage = "Net planned spending must not exceed net planned income.";

    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static Budget Create(string name, string currency) =>
        new()
        {
            Name = name.Trim(),
            Currency = new Currency(currency).Value
        };

    public bool CanRecordAdjustment(IReadOnlyCollection<BudgetAdjustment> existingAdjustments, BudgetAdjustment pendingAdjustment)
    {
        var netPlannedAmount = existingAdjustments
            .Where(x => x.Date <= pendingAdjustment.Date)
            .Sum(x => x.SignedPlannedAmount());

        return netPlannedAmount + pendingAdjustment.SignedPlannedAmount() >= 0;
    }

    public DomainEvent CreatedEvent() =>
        new(
            "BudgetCreated",
            Id,
            nameof(Budget),
            Id,
            $"Created budget {Name}.",
            Payload: new BudgetCreatedPayload(Id, Name, Currency));
}
