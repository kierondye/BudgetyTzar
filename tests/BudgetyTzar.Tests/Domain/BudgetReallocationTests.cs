using BudgetyTzar.Api;
using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Tests;

public sealed class BudgetReallocationTests
{
    [Fact]
    public void BudgetReallocationDoesNotExposePublicMutationOrConstruction()
    {
        var publicConstructors = typeof(BudgetReallocation).GetConstructors();
        var mutableProperties = typeof(BudgetReallocation)
            .GetProperties()
            .Where(x => x.SetMethod?.IsPublic == true)
            .Select(x => x.Name)
            .ToList();

        Assert.Empty(publicConstructors);
        Assert.Empty(mutableProperties);
    }

    [Fact]
    public void ReallocationRequiresAtLeastTwoAdjustments()
    {
        var validationError = BudgetReallocation.ValidateAdjustments([
            new BudgetReallocationAdjustment(Guid.NewGuid(), 10m, BudgetAdjustmentType.Credit)
        ]);

        Assert.Equal("A reallocation must contain at least two adjustments.", validationError);
    }

    [Fact]
    public void ReallocationRequiresCreditsToEqualDebits()
    {
        var validationError = BudgetReallocation.ValidateAdjustments([
            new BudgetReallocationAdjustment(Guid.NewGuid(), 30m, BudgetAdjustmentType.Credit),
            new BudgetReallocationAdjustment(Guid.NewGuid(), 20m, BudgetAdjustmentType.Debit)
        ]);

        Assert.Equal("Reallocation credits must equal reallocation debits.", validationError);
    }

    [Fact]
    public void ReallocationReturnsValidationResultWhenLinkedAdjustmentsAreInvalid()
    {
        var reallocation = BudgetReallocation.Create(Guid.NewGuid(), new DateOnly(2026, 6, 5), "Move budget");

        var result = reallocation.CreateLinkedAdjustments([
            new BudgetReallocationAdjustment(Guid.NewGuid(), 30m, BudgetAdjustmentType.Credit),
            new BudgetReallocationAdjustment(Guid.NewGuid(), 20m, BudgetAdjustmentType.Debit)
        ]);

        var validationFailed = Assert.IsType<CreateLinkedBudgetAdjustmentsResult.ValidationFailed>(result);
        Assert.Equal("Reallocation credits must equal reallocation debits.", validationFailed.Error);
    }

    [Fact]
    public void ReallocationRequiresConsumptionBudgetItems()
    {
        var budgetId = Guid.NewGuid();
        var validationError = BudgetReallocation.ValidateBudgetItems([
            BudgetItem.Create(budgetId, "Salary", BudgetItemKind.Funding),
            BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption)
        ]);

        Assert.Equal(BudgetReallocation.ConsumptionItemsOnlyMessage, validationError);
    }

    [Fact]
    public void ReallocationCreatesLinkedBudgetAdjustments()
    {
        var budgetId = Guid.NewGuid();
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 5);
        var reallocation = BudgetReallocation.Create(budgetId, date, " Move budget ");

        var result = reallocation.CreateLinkedAdjustments([
            new BudgetReallocationAdjustment(firstItemId, 30m, BudgetAdjustmentType.Credit),
            new BudgetReallocationAdjustment(secondItemId, 30m, BudgetAdjustmentType.Debit)
        ]);
        var success = Assert.IsType<CreateLinkedBudgetAdjustmentsResult.Success>(result);
        var adjustments = success.Adjustments;

        Assert.Equal(2, adjustments.Count);
        Assert.All(adjustments, x =>
        {
            Assert.Equal(budgetId, x.BudgetId);
            Assert.Equal(reallocation.Id, x.ReallocationId);
            Assert.Equal(date, x.Date);
            Assert.Equal("Move budget", x.Notes);
        });
        Assert.Contains(adjustments, x => x.BudgetItemId == firstItemId && x.Amount == 30m && x.Type == BudgetAdjustmentType.Credit);
        Assert.Contains(adjustments, x => x.BudgetItemId == secondItemId && x.Amount == 30m && x.Type == BudgetAdjustmentType.Debit);
    }

    [Fact]
    public void ReallocationRecordedEventUsesReallocationBudgetId()
    {
        var budgetId = Guid.NewGuid();
        var budgetItemId = Guid.NewGuid();
        var reallocation = BudgetReallocation.Create(budgetId, new DateOnly(2026, 6, 5), "Move budget");

        var domainEvent = reallocation.RecordedEvent([
            new BudgetReallocationAdjustment(budgetItemId, 30m, BudgetAdjustmentType.Credit),
            new BudgetReallocationAdjustment(Guid.NewGuid(), 30m, BudgetAdjustmentType.Debit)
        ]);

        Assert.Equal("BudgetReallocationRecorded", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(reallocation.Id, domainEvent.EntityId);
        var payload = Assert.IsType<BudgetReallocationRecordedPayload>(domainEvent.Payload);
        Assert.Equal(budgetId, payload.BudgetId);
        Assert.Equal(reallocation.Id, payload.BudgetReallocationId);
        Assert.Contains(payload.Adjustments, x => x.BudgetItemId == budgetItemId);
    }
}
