using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Tests;

public sealed class TransactionEditingTests
{
    [Fact]
    public async Task TransactionEditProjectsAuditAndPreservesAllocations()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries", BudgetItemKind.Consumption);
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, 25m, TransactionDirection.Debit);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, transaction.Id, [new TransactionAllocationItem(groceries.Id, 20m)]);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}",
            new UpdateTransactionRequest(
                new DateOnly(2026, 6, 11),
                "Edited groceries",
                30m,
                TransactionDirection.Debit,
                "Current account",
                "EDIT-1",
                "Updated note"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var edited = await app.GetTransactionAsync(transaction.Id);
        Assert.Equal(transaction.Id, edited!.Id);
        Assert.Equal("Edited groceries", edited.Description);
        Assert.Equal(30m, edited.Amount);
        Assert.Equal(1, await app.CountAllocationsAsync(transaction.Id));

        await app.ProjectAuditEventsAsync(budget.Id);
        var audit = await app.GetAuditEventsAsync(budget.Id);
        var editAudit = audit.Single(x => x.EventType == "TransactionEdited");
        Assert.NotEqual(Guid.Empty, editAudit.Id);
        Assert.Equal(nameof(FinancialTransaction), editAudit.EntityType);
        Assert.Equal(transaction.Id, editAudit.EntityId);
    }

    [Fact]
    public async Task TransactionAmountCannotBeEditedBelowCurrentAllocationTotal()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries", BudgetItemKind.Consumption);
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, 25m, TransactionDirection.Debit);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, transaction.Id, [new TransactionAllocationItem(groceries.Id, 20m)]);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}",
            new UpdateTransactionRequest(
                transaction.TransactionDate,
                transaction.Description,
                19.99m,
                transaction.Direction,
                transaction.SourceAccount,
                transaction.ExternalReference,
                transaction.Notes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, await app.CountAllocationsAsync(transaction.Id));
        var persisted = await app.GetTransactionAsync(transaction.Id);
        Assert.Equal(25m, persisted!.Amount);
    }
}
