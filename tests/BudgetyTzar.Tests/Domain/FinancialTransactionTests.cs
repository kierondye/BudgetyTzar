using BudgetyTzar.Api;
using BudgetyTzar.Api.Contracts.Events;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Tests;

public sealed class FinancialTransactionTests
{
    [Fact]
    public void TransactionCreateProducesCreatedEvent()
    {
        var budgetId = Guid.NewGuid();
        var transaction = FinancialTransaction.Create(
            budgetId,
            new DateOnly(2026, 6, 10),
            " Manual transaction ",
            25m,
            TransactionDirection.Debit,
            "Current account",
            "MANUAL-1",
            "Entered by hand");

        var domainEvent = transaction.CreatedEvent();

        Assert.Equal("Manual transaction", transaction.Description);
        Assert.False(transaction.IsIgnored);
        Assert.Equal("TransactionManuallyCreated", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(transaction.Id, domainEvent.EntityId);
        Assert.Equal("Created transaction Manual transaction for 25 Debit.", domainEvent.Description);
        var payload = Assert.IsType<TransactionManuallyCreatedPayload>(domainEvent.Payload);
        Assert.Equal(transaction.Id, payload.TransactionId);
        Assert.Equal(budgetId, payload.BudgetId);
        Assert.Equal(new DateOnly(2026, 6, 10), payload.TransactionDate);
        Assert.Equal("Manual transaction", payload.Description);
        Assert.Equal(25m, payload.Amount);
        Assert.Equal(TransactionDirection.Debit, payload.Direction);
        Assert.Equal("Current account", payload.SourceAccount);
        Assert.Equal("MANUAL-1", payload.ExternalReference);
        Assert.Equal("Entered by hand", payload.Notes);
        Assert.False(payload.IsIgnored);
    }

    [Fact]
    public void TransactionRejectsAllocationsAboveTransactionAmount()
    {
        var transaction = FinancialTransaction.Create(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 10),
            "Groceries",
            25m,
            TransactionDirection.Debit,
            null,
            null,
            null);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            transaction.ReplaceAllocations([
                new TransactionAllocationItem(Guid.NewGuid(), 20m),
                new TransactionAllocationItem(Guid.NewGuid(), 5.01m)
            ]));

        Assert.Equal("Total allocated amount cannot exceed the transaction amount.", exception.Message);
    }

    [Fact]
    public void TransactionRejectsDuplicateBudgetItemAllocations()
    {
        var transaction = FinancialTransaction.Create(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 10),
            "Groceries",
            25m,
            TransactionDirection.Debit,
            null,
            null,
            null);
        var budgetItemId = Guid.NewGuid();

        var validationError = transaction.ValidateReplacementAllocations([
            new TransactionAllocationItem(budgetItemId, 10m),
            new TransactionAllocationItem(budgetItemId, 5m)
        ]);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            transaction.ReplaceAllocations([
                new TransactionAllocationItem(budgetItemId, 10m),
                new TransactionAllocationItem(budgetItemId, 5m)
            ]));

        Assert.Equal(FinancialTransaction.DuplicateBudgetItemAllocationMessage, validationError);
        Assert.Equal(FinancialTransaction.DuplicateBudgetItemAllocationMessage, exception.Message);
    }

    [Fact]
    public void TransactionCanCreateSplitAllocationsWithinAmount()
    {
        var transaction = FinancialTransaction.Create(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 10),
            "Split shop",
            30m,
            TransactionDirection.Debit,
            null,
            null,
            null);
        var groceries = Guid.NewGuid();
        var household = Guid.NewGuid();

        var allocations = transaction.ReplaceAllocations([
            new TransactionAllocationItem(groceries, 20m),
            new TransactionAllocationItem(household, 10m)
        ]);

        Assert.Equal(2, allocations.Count);
        Assert.Equal(30m, allocations.Sum(x => x.Amount));
        Assert.Contains(allocations, x => x.BudgetItemId == groceries);
        Assert.Contains(allocations, x => x.BudgetItemId == household);
    }

    [Fact]
    public void TransactionAllocationsTrimOptionalNotes()
    {
        var transaction = FinancialTransaction.Create(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 10),
            "Split shop",
            30m,
            TransactionDirection.Debit,
            null,
            null,
            null);

        var allocation = Assert.Single(transaction.ReplaceAllocations([
            new TransactionAllocationItem(Guid.NewGuid(), 20m, "  Weekly shop  ")
        ]));

        Assert.Equal("Weekly shop", allocation.Notes);
    }

    [Fact]
    public void TransactionAllocationReplacementProducesReplacedEvent()
    {
        var budgetId = Guid.NewGuid();
        var transaction = FinancialTransaction.Create(
            budgetId,
            new DateOnly(2026, 6, 10),
            "Split shop",
            50m,
            TransactionDirection.Debit,
            null,
            null,
            null);
        var existingItemId = Guid.NewGuid();
        var newItemId = Guid.NewGuid();
        var existing = new[]
        {
            TransactionAllocation.Create(transaction.Id, existingItemId, 20m, "Old note")
        };
        var replacements = new[]
        {
            new TransactionAllocationItem(newItemId, 30m, "  Weekly shop  ")
        };

        var domainEvent = transaction.AllocationsReplacedEvent(existing, replacements);

        Assert.Equal("TransactionAllocationsReplaced", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(transaction.Id, domainEvent.EntityId);
        Assert.Equal("Allocated transaction Split shop.", domainEvent.Description);
        Assert.Equal($"Previous={existingItemId}:20 (Old note); New={newItemId}:30 (Weekly shop)", domainEvent.Details);
        var payload = Assert.IsType<TransactionAllocationsReplacedPayload>(domainEvent.Payload);
        Assert.Equal(transaction.Id, payload.TransactionId);
        Assert.Equal(budgetId, payload.BudgetId);
        Assert.Equal(50m, payload.TransactionAmount);
        var allocation = Assert.Single(payload.Allocations);
        Assert.Equal(newItemId, allocation.BudgetItemId);
        Assert.Equal(30m, allocation.Amount);
        Assert.Equal("Weekly shop", allocation.Notes);
    }

    [Fact]
    public void TransactionAllocationClearingProducesClearedEvent()
    {
        var budgetId = Guid.NewGuid();
        var transaction = FinancialTransaction.Create(
            budgetId,
            new DateOnly(2026, 6, 10),
            "Split shop",
            50m,
            TransactionDirection.Debit,
            null,
            null,
            null);
        var existingItemId = Guid.NewGuid();
        var existing = new[]
        {
            TransactionAllocation.Create(transaction.Id, existingItemId, 20m, "Old note")
        };

        var domainEvent = transaction.AllocationsClearedEvent(existing);

        Assert.Equal("TransactionAllocationsCleared", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(transaction.Id, domainEvent.EntityId);
        Assert.Equal("Cleared allocations for transaction Split shop.", domainEvent.Description);
        Assert.Equal($"{existingItemId}:20 (Old note)", domainEvent.Details);
        var payload = Assert.IsType<TransactionAllocationsClearedPayload>(domainEvent.Payload);
        Assert.Equal(transaction.Id, payload.TransactionId);
        Assert.Equal(budgetId, payload.BudgetId);
        var allocation = Assert.Single(payload.ClearedAllocations);
        Assert.Equal(existingItemId, allocation.BudgetItemId);
        Assert.Equal(20m, allocation.Amount);
        Assert.Equal("Old note", allocation.Notes);
    }

    [Fact]
    public void TransactionEditRejectsAmountBelowCurrentAllocatedTotal()
    {
        var transaction = FinancialTransaction.Create(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 10),
            "Groceries",
            25m,
            TransactionDirection.Debit,
            null,
            null,
            null);

        var validationError = transaction.ValidateEdit(19.99m, 20m);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            transaction.Edit(
                transaction.TransactionDate,
                transaction.Description,
                19.99m,
                transaction.Direction,
                transaction.SourceAccount,
                transaction.ExternalReference,
                transaction.Notes,
                20m));

        Assert.Equal(FinancialTransaction.AmountBelowAllocatedTotalMessage, validationError);
        Assert.Equal(FinancialTransaction.AmountBelowAllocatedTotalMessage, exception.Message);
        Assert.Equal(25m, transaction.Amount);
    }

    [Fact]
    public void TransactionEditChangesStateAndProducesEditedEvent()
    {
        var budgetId = Guid.NewGuid();
        var transaction = FinancialTransaction.Create(
            budgetId,
            new DateOnly(2026, 6, 10),
            "Groceries",
            25m,
            TransactionDirection.Debit,
            null,
            null,
            null);

        var domainEvent = transaction.Edit(
            new DateOnly(2026, 6, 11),
            " Edited groceries ",
            30m,
            TransactionDirection.Credit,
            "Current account",
            "EDIT-1",
            "Updated note",
            20m);

        Assert.Equal("Edited groceries", transaction.Description);
        Assert.Equal(30m, transaction.Amount);
        Assert.Equal(TransactionDirection.Credit, transaction.Direction);
        Assert.Equal("TransactionEdited", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(transaction.Id, domainEvent.EntityId);
        Assert.Equal("Edited transaction Edited groceries.", domainEvent.Description);
        Assert.Equal("Previous=Groceries, 25 Debit; New=Edited groceries, 30 Credit", domainEvent.Details);
        var payload = Assert.IsType<TransactionEditedPayload>(domainEvent.Payload);
        Assert.Equal(transaction.Id, payload.TransactionId);
        Assert.Equal(budgetId, payload.BudgetId);
        Assert.Equal(new DateOnly(2026, 6, 11), payload.TransactionDate);
        Assert.Equal("Edited groceries", payload.Description);
        Assert.Equal(30m, payload.Amount);
        Assert.Equal(TransactionDirection.Credit, payload.Direction);
        Assert.Equal("Current account", payload.SourceAccount);
        Assert.Equal("EDIT-1", payload.ExternalReference);
        Assert.Equal("Updated note", payload.Notes);
        Assert.False(payload.IsIgnored);
    }

    [Fact]
    public void TransactionIgnoreChangesStateAndProducesIgnoredEvent()
    {
        var budgetId = Guid.NewGuid();
        var transaction = FinancialTransaction.Create(
            budgetId,
            new DateOnly(2026, 6, 10),
            "Duplicate transaction",
            25m,
            TransactionDirection.Debit,
            "Current account",
            "DUP-1",
            "Imported twice");

        var domainEvent = transaction.Ignore();

        Assert.True(transaction.IsIgnored);
        Assert.Equal("TransactionIgnored", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(transaction.Id, domainEvent.EntityId);
        Assert.Equal("Ignored transaction Duplicate transaction.", domainEvent.Description);
        var payload = Assert.IsType<TransactionIgnoredPayload>(domainEvent.Payload);
        Assert.Equal(transaction.Id, payload.TransactionId);
        Assert.Equal(budgetId, payload.BudgetId);
        Assert.Equal(new DateOnly(2026, 6, 10), payload.TransactionDate);
        Assert.Equal("Duplicate transaction", payload.Description);
        Assert.Equal(25m, payload.Amount);
        Assert.Equal(TransactionDirection.Debit, payload.Direction);
        Assert.Equal("Current account", payload.SourceAccount);
        Assert.Equal("DUP-1", payload.ExternalReference);
        Assert.Equal("Imported twice", payload.Notes);
        Assert.True(payload.IsIgnored);
    }
}
