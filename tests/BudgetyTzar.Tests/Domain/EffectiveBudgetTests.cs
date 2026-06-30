using BudgetyTzar.Api;
using BudgetyTzar.Api.Contracts.Events;

namespace BudgetyTzar.Tests;

public sealed class EffectiveBudgetTests
{
    [Fact]
    public void EffectiveBudgetItemStateIsNotPartOfThePublicCommandSurface()
    {
        var exportedTypes = typeof(EffectiveBudget).Assembly.GetExportedTypes();

        Assert.DoesNotContain(exportedTypes, x => x.Name == nameof(EffectiveBudgetItemState));
    }

    [Fact]
    public void EffectiveBudgetAllowsDebitAdjustmentWhenPlannedFundingCoversItByDate()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 7, 2);
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            100m,
            [new EffectiveBudgetItemState(groceries, 0m)]);

        var result = effectiveBudget.RecordAdjustment(groceries.Id, 100m, BudgetAdjustmentType.Debit, "Covered spending");

        var success = Assert.IsType<EffectiveBudgetResult.Success>(result);
        Assert.Same(effectiveBudget, success.Budget);
        var adjustment = Assert.Single(success.Budget.PendingAdjustments);
        Assert.Equal(budgetId, adjustment.BudgetId);
        Assert.Equal(groceries.Id, adjustment.BudgetItemId);
        Assert.Equal(date, adjustment.Date);
        Assert.Equal(100m, adjustment.Amount);
        Assert.Equal(BudgetAdjustmentType.Debit, adjustment.Type);
        Assert.Equal("Covered spending", adjustment.Notes);

        var domainEvent = Assert.Single(success.Budget.PendingEvents);
        Assert.Equal("BudgetAdjustmentRecorded", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(nameof(BudgetAdjustment), domainEvent.EntityType);
        Assert.Equal(adjustment.Id, domainEvent.EntityId);
        var payload = Assert.IsType<BudgetAdjustmentRecordedPayload>(domainEvent.Payload);
        Assert.Equal(adjustment.Id, payload.BudgetAdjustmentId);
        Assert.Equal(budgetId, payload.BudgetId);
        Assert.Equal(groceries.Id, payload.BudgetItemId);
        Assert.Equal(100m, payload.Amount);
        Assert.Equal(BudgetAdjustmentType.Debit, payload.Direction);
        Assert.Equal(date, payload.Date);
        Assert.Equal("Covered spending", payload.Notes);
    }

    [Fact]
    public void EffectiveBudgetRejectsDebitAdjustmentWhenPlannedFundingDoesNotCoverItByDate()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 1);
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            0m,
            [new EffectiveBudgetItemState(groceries, 0m)]);

        var result = effectiveBudget.RecordAdjustment(groceries.Id, 100m, BudgetAdjustmentType.Debit, "Too early");

        var validationProblem = Assert.IsType<EffectiveBudgetResult.ValidationFailed>(result);
        Assert.Equal(EffectiveBudget.NetPlannedSpendingExceededMessage, validationProblem.Error);
        Assert.Empty(effectiveBudget.PendingAdjustments);
        Assert.Empty(effectiveBudget.PendingEvents);
    }

    [Fact]
    public void EffectiveBudgetRejectsTooPreciseDecimalAmountWithoutRecordingAdjustment()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 7, 2);
        var salary = BudgetItem.Create(budgetId, "Salary", BudgetItemKind.Funding);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            0m,
            [new EffectiveBudgetItemState(salary, 0m)]);

        var result = effectiveBudget.RecordAdjustment(salary.Id, 10.001m, BudgetAdjustmentType.Credit, "Too precise");

        var validationProblem = Assert.IsType<EffectiveBudgetResult.ValidationFailed>(result);
        Assert.Equal(EffectiveBudget.MoneyScaleExceededMessage, validationProblem.Error);
        Assert.Empty(effectiveBudget.PendingAdjustments);
        Assert.Empty(effectiveBudget.PendingEvents);
    }

    [Fact]
    public void EffectiveBudgetRejectsConsumptionAdjustmentThatWouldBecomeFunding()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 2);
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            25m,
            [new EffectiveBudgetItemState(groceries, -75m)]);

        var result = effectiveBudget.RecordAdjustment(groceries.Id, 100m, BudgetAdjustmentType.Credit, "Correction");

        var validationProblem = Assert.IsType<EffectiveBudgetResult.ValidationFailed>(result);
        Assert.Equal(BudgetItem.ConsumptionBudgetItemBecameFundingMessage, validationProblem.Error);
    }

    [Fact]
    public void EffectiveBudgetAllowsConsumptionCreditCorrectionWithinExistingConsumptionBudget()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 2);
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            0m,
            [new EffectiveBudgetItemState(groceries, -100m)]);

        var result = effectiveBudget.RecordAdjustment(groceries.Id, 25m, BudgetAdjustmentType.Credit, "Correction");

        var success = Assert.IsType<EffectiveBudgetResult.Success>(result);
        var adjustment = Assert.Single(success.Budget.PendingAdjustments);
        Assert.Equal(date, adjustment.Date);
        Assert.Equal(groceries.Id, adjustment.BudgetItemId);
    }

    [Fact]
    public void EffectiveBudgetRejectsFundingAdjustmentThatWouldBecomeConsumption()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 2);
        var salary = BudgetItem.Create(budgetId, "Salary", BudgetItemKind.Funding);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            200m,
            [new EffectiveBudgetItemState(salary, 75m)]);

        var result = effectiveBudget.RecordAdjustment(salary.Id, 100m, BudgetAdjustmentType.Debit, "Reversal");

        var validationProblem = Assert.IsType<EffectiveBudgetResult.ValidationFailed>(result);
        Assert.Equal(BudgetItem.FundingBudgetItemBecameConsumptionMessage, validationProblem.Error);
    }

    [Fact]
    public void EffectiveBudgetAllowsFundingDebitCorrectionWithinExistingFundingBudget()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 2);
        var salary = BudgetItem.Create(budgetId, "Salary", BudgetItemKind.Funding);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            100m,
            [new EffectiveBudgetItemState(salary, 100m)]);

        var result = effectiveBudget.RecordAdjustment(salary.Id, 25m, BudgetAdjustmentType.Debit, "Reversal");

        var success = Assert.IsType<EffectiveBudgetResult.Success>(result);
        var adjustment = Assert.Single(success.Budget.PendingAdjustments);
        Assert.Equal(date, adjustment.Date);
        Assert.Equal(salary.Id, adjustment.BudgetItemId);
    }

    [Fact]
    public void EffectiveBudgetReturnsNotFoundWhenRecordingAdjustmentForUnknownItem()
    {
        var effectiveBudget = new EffectiveBudget(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 2),
            0m,
            []);

        var unknownItemId = Guid.NewGuid();

        var result = effectiveBudget.RecordAdjustment(unknownItemId, 25m, BudgetAdjustmentType.Credit, "Unknown item");

        var notFound = Assert.IsType<EffectiveBudgetResult.ItemNotFound>(result);
        Assert.Equal(unknownItemId, notFound.BudgetItemId);
        Assert.Empty(effectiveBudget.PendingAdjustments);
        Assert.Empty(effectiveBudget.PendingEvents);
    }

    [Fact]
    public void EffectiveBudgetRejectsAdjustmentForArchivedItemAfterArchiveDate()
    {
        var budgetId = Guid.NewGuid();
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        groceries.Archive(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            new DateOnly(2026, 6, 2),
            100m,
            [new EffectiveBudgetItemState(groceries, 0m)]);

        var result = effectiveBudget.RecordAdjustment(groceries.Id, 25m, BudgetAdjustmentType.Debit, "After archive");

        var archived = Assert.IsType<EffectiveBudgetResult.ItemArchived>(result);
        Assert.Equal(groceries.Id, archived.BudgetItemId);
        Assert.Empty(effectiveBudget.PendingAdjustments);
        Assert.Empty(effectiveBudget.PendingEvents);
    }

    [Fact]
    public void EffectiveBudgetUpdatesEffectivePlannedAmountAfterSuccessfulAdjustment()
    {
        var budgetId = Guid.NewGuid();
        var date = new DateOnly(2026, 7, 2);
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            100m,
            [new EffectiveBudgetItemState(groceries, 0m)]);

        var firstResult = effectiveBudget.RecordAdjustment(groceries.Id, 75m, BudgetAdjustmentType.Debit, "Covered spending");
        Assert.IsType<EffectiveBudgetResult.Success>(firstResult);

        var secondResult = effectiveBudget.RecordAdjustment(groceries.Id, 50m, BudgetAdjustmentType.Debit, "Too much");

        var validationProblem = Assert.IsType<EffectiveBudgetResult.ValidationFailed>(secondResult);
        Assert.Equal(EffectiveBudget.NetPlannedSpendingExceededMessage, validationProblem.Error);
        Assert.Equal(25m, effectiveBudget.NetPlannedAmount);
        Assert.Single(effectiveBudget.PendingAdjustments);
        Assert.Single(effectiveBudget.PendingEvents);
    }

    [Fact]
    public void EffectiveBudgetRejectsItemsFromAnotherBudget()
    {
        var budgetId = Guid.NewGuid();
        var otherBudgetItem = BudgetItem.Create(Guid.NewGuid(), "Groceries", BudgetItemKind.Consumption);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EffectiveBudget(
                budgetId,
                new DateOnly(2026, 6, 2),
                0m,
                [new EffectiveBudgetItemState(otherBudgetItem, 0m)]));

        Assert.Equal("Effective budget items must belong to the effective budget.", exception.Message);
    }
}
