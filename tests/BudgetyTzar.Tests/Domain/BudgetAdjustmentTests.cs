using BudgetyTzar.Api;
using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Tests;

public sealed class BudgetAdjustmentTests
{
    [Fact]
    public void SignedPlannedAmountTreatsCreditsAsPositiveAndDebitsAsNegative()
    {
        var budgetId = Guid.NewGuid();
        var budgetItemId = Guid.NewGuid();

        var credit = BudgetAdjustment.Create(
            budgetId,
            budgetItemId,
            100m,
            BudgetAdjustmentType.Credit,
            new DateOnly(2026, 6, 1),
            null);
        var debit = BudgetAdjustment.Create(
            budgetId,
            budgetItemId,
            25m,
            BudgetAdjustmentType.Debit,
            new DateOnly(2026, 6, 1),
            null);

        Assert.Equal(100m, credit.SignedPlannedAmount());
        Assert.Equal(-25m, debit.SignedPlannedAmount());
    }

    [Fact]
    public void AdjustmentRecordedEventUsesAdjustmentBudgetId()
    {
        var budgetId = Guid.NewGuid();
        var budgetItemId = Guid.NewGuid();
        var adjustment = BudgetAdjustment.Create(
            budgetId,
            budgetItemId,
            100m,
            BudgetAdjustmentType.Credit,
            new DateOnly(2026, 6, 1),
            "Initial balance");

        var domainEvent = adjustment.RecordedEvent("Salary");

        Assert.Equal("BudgetAdjustmentRecorded", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(adjustment.Id, domainEvent.EntityId);
        var payload = Assert.IsType<BudgetAdjustmentRecordedPayload>(domainEvent.Payload);
        Assert.Equal(budgetId, payload.BudgetId);
        Assert.Equal(budgetItemId, payload.BudgetItemId);
    }
}
