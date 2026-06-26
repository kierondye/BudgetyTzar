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
