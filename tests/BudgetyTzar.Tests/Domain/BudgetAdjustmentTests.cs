using BudgetyTzar.Api;
using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Tests;

public sealed class BudgetAdjustmentTests
{
    [Fact]
    public void BudgetAdjustmentDoesNotExposePublicMutationOrConstruction()
    {
        var publicConstructors = typeof(BudgetAdjustment).GetConstructors();
        var mutableProperties = typeof(BudgetAdjustment)
            .GetProperties()
            .Where(x => x.SetMethod?.IsPublic == true)
            .Select(x => x.Name)
            .ToList();

        Assert.Empty(publicConstructors);
        Assert.Empty(mutableProperties);
    }

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
        Assert.Equal(adjustment.Id, payload.BudgetAdjustmentId);
        Assert.Equal(100m, payload.Amount);
        Assert.Equal(BudgetAdjustmentType.Credit, payload.Direction);
        Assert.Equal(new DateOnly(2026, 6, 1), payload.Date);
        Assert.Equal("Initial balance", payload.Notes);
    }
}
