using System.Net.Http.Json;
using System.Text.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests;

public sealed class AuditEventProjectionTests
{
    [Fact]
    public async Task AuditProjectionWritesLegacyAuditEventsForCanonicalEventFamilies()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary");
        await BudgetApiTestClient.ArchiveBudgetItem(client, budget.Id, salary.Id);
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, groceries.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 1), "Initial groceries.");
        var reallocationResponse = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/reallocations",
            new CreateBudgetItemReallocationRequest(
                new DateOnly(2026, 6, 2),
                "Move funds",
                [
                    new BudgetReallocationAdjustmentItem(groceries.Id, 10m, BudgetAdjustmentType.Debit),
                    new BudgetReallocationAdjustmentItem(salary.Id, 10m, BudgetAdjustmentType.Credit)
                ]));
        reallocationResponse.EnsureSuccessStatusCode();
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 3), 25m, TransactionDirection.Debit);
        await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}",
            new UpdateTransactionRequest(
                transaction.TransactionDate,
                "Edited transaction",
                transaction.Amount,
                transaction.Direction,
                transaction.SourceAccount,
                transaction.ExternalReference,
                transaction.Notes));
        await BudgetApiTestClient.IgnoreTransaction(client, budget.Id, transaction.Id);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, transaction.Id, [new TransactionAllocationItem(groceries.Id, 25m)]);
        await BudgetApiTestClient.ClearAllocations(client, budget.Id, transaction.Id);

        await app.ProjectAuditEventsAsync(budget.Id);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var outboxIds = await db.OutboxMessages.AsNoTracking()
            .Where(x => x.BudgetId == budget.Id)
            .Select(x => x.Id)
            .ToListAsync();
        var auditEvents = await db.AuditEvents.AsNoTracking()
            .Where(x => x.BudgetId == budget.Id)
            .ToListAsync();

        Assert.All(outboxIds, id => Assert.Contains(auditEvents, x => x.Id == id));
        Assert.Contains(auditEvents, x => x.EventType == "BudgetCreated" && x.Description.StartsWith("Created budget "));
        Assert.Contains(auditEvents, x => x.EventType == "BudgetItemCreated" && x.Description.StartsWith("Created budget item "));
        Assert.Contains(auditEvents, x => x.EventType == "BudgetItemArchived" && x.Description.StartsWith("Archived budget item "));
        Assert.Contains(auditEvents, x => x.EventType == "BudgetAdjustmentRecorded" && x.Description.StartsWith("Recorded credit adjustment "));
        Assert.Contains(auditEvents, x => x.EventType == "BudgetReallocationRecorded" && x.Description == "Recorded budget reallocation.");
        Assert.Contains(auditEvents, x => x.EventType == "TransactionManuallyCreated" && x.Description.StartsWith("Created transaction "));
        Assert.Contains(auditEvents, x => x.EventType == "TransactionEdited" && x.Description == "Edited transaction Edited transaction.");
        Assert.Contains(auditEvents, x => x.EventType == "TransactionIgnored" && x.Description == "Ignored transaction Edited transaction.");
        Assert.Contains(auditEvents, x => x.EventType == "TransactionAllocationsReplaced" && x.Description == "Replaced transaction allocations.");
        Assert.Contains(auditEvents, x => x.EventType == "TransactionAllocationsCleared" && x.Description == "Cleared transaction allocations.");
        Assert.All(auditEvents, x =>
        {
            Assert.NotNull(x.Details);
            Assert.Contains("budgetId", x.Details);
        });
    }

    [Fact]
    public async Task AuditProjectionIsIdempotentForDuplicateEvents()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var envelopeJson = await db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.BudgetId == budget.Id)
            .Select(x => x.EnvelopeJson)
            .SingleAsync();
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(envelopeJson, EventSerialization.Options)!;
        var auditor = scope.ServiceProvider.GetRequiredService<AuditEventConsumerService>();

        await auditor.ProjectEnvelope(envelopeJson, CancellationToken.None);
        await auditor.ProjectEnvelope(envelopeJson, CancellationToken.None);

        Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.Id == envelope.EventId));
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(x => x.Id == envelope.EventId);
        Assert.Equal("BudgetCreated", audit.EventType);
        Assert.Equal(nameof(Budget), audit.EntityType);
        Assert.Equal(budget.Id, audit.EntityId);
    }
}
