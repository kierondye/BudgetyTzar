using System.Net.Http.Json;
using System.Text.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests;

public sealed class AuditAndOutboxTests
{
    [Fact]
    public async Task CommandsWriteAuditRecordsAndDomainShapedOutboxEnvelopesAtomically()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var budget = await BudgetApiTestClient.CreateBudget(client);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(x => x.EntityId == budget.Id);
        var outbox = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.AggregateId == budget.Id);
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(outbox.EnvelopeJson, EventSerialization.Options)!;

        Assert.Equal("BudgetCreated", audit.EventType);
        Assert.Equal("budgetytzar.budgeting.budget-created.v1", outbox.EventType);
        Assert.Equal("budgetytzar.budgeting.events", outbox.Topic);
        Assert.Equal(outbox.Id, envelope.EventId);
        Assert.Equal(outbox.EventType, envelope.EventType);
        Assert.Equal(budget.Id, envelope.Payload["budgetId"]!.GetValue<Guid>());
        Assert.Equal(budget.Name, envelope.Payload["name"]!.GetValue<string>());
        Assert.Equal(budget.Currency, envelope.Payload["currency"]!.GetValue<string>());
        Assert.Equal(audit.Id, envelope.Payload["auditEventId"]!.GetValue<Guid>());
        Assert.False(envelope.Payload.ContainsKey("auditEventType"));
        Assert.False(envelope.Payload.ContainsKey("auditDescription"));
        Assert.False(envelope.Payload.ContainsKey("auditDetails"));
    }

    [Fact]
    public async Task AllocationReplacementWorkflowsEmitReplacedAndDeleteEmitsCleared()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var savings = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Savings");
        var household = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Household");
        var single = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 12), 35m, TransactionDirection.Debit);
        var multi = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 13), 40m, TransactionDirection.Debit);
        var manyToOne = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 14), 50m, TransactionDirection.Debit);
        var empty = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 15), 20m, TransactionDirection.Debit);

        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, single.Id, [new TransactionAllocationItem(groceries.Id, 35m)]);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, multi.Id, [
            new TransactionAllocationItem(groceries.Id, 15m),
            new TransactionAllocationItem(savings.Id, 25m)
        ]);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, manyToOne.Id, [
            new TransactionAllocationItem(groceries.Id, 20m),
            new TransactionAllocationItem(savings.Id, 20m)
        ]);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, manyToOne.Id, [new TransactionAllocationItem(household.Id, 50m)]);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, empty.Id, []);
        await BudgetApiTestClient.ClearAllocations(client, budget.Id, multi.Id);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var outboxEventTypes = await db.OutboxMessages.AsNoTracking()
            .Select(x => x.EventType)
            .ToListAsync();
        var auditEvents = await db.AuditEvents.AsNoTracking()
            .Where(x => x.EntityId == single.Id || x.EntityId == multi.Id || x.EntityId == manyToOne.Id || x.EntityId == empty.Id)
            .ToListAsync();

        Assert.Equal(5, outboxEventTypes.Count(x => x == "budgetytzar.transactions.transaction-allocations-replaced.v1"));
        Assert.Single(outboxEventTypes, x => x == "budgetytzar.transactions.transaction-allocations-cleared.v1");
        Assert.DoesNotContain(outboxEventTypes, x => x == "budgetytzar.transactions.transaction-allocation-recorded.v1");
        Assert.Equal(5, auditEvents.Count(x => x.EventType == "TransactionAllocationsReplaced"));
        Assert.Single(auditEvents, x => x.EventType == "TransactionAllocationsCleared");
        Assert.DoesNotContain(auditEvents, x => x.EventType == "TransactionAllocationRecorded");
        Assert.All(auditEvents.Where(x => x.EventType == "TransactionAllocationsReplaced"), x =>
        {
            Assert.StartsWith("Allocated transaction ", x.Description);
            Assert.DoesNotContain("split", x.Description, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task AuditTimelineEndpointFallsBackToDurableAuditRecordsWhenProjectionReportsAreDisabled()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: false);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);

        var apiAuditEvents = await client.GetFromJsonAsync<IReadOnlyList<AuditEventDto>>(
            $"/api/budgets/{budget.Id}/audit-events");

        Assert.Contains(apiAuditEvents!, x => x.EventType == "BudgetCreated");
    }

    [Fact]
    public async Task AuditEventsEndpointReturnsDurableLocalAuditTimeline()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, groceries.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 1), "Initial groceries.");

        var events = await client.GetFromJsonAsync<IReadOnlyList<AuditEventDto>>(
            $"/api/budgets/{budget.Id}/audit-events");

        Assert.NotNull(events);
        Assert.Contains(events!, x => x.EventType == "BudgetCreated" && x.EntityId == budget.Id);
        Assert.Contains(events!, x => x.EventType == "BudgetItemCreated" && x.EntityId == groceries.Id);
        Assert.Contains(events!, x => x.EventType == "BudgetAdjustmentRecorded");
    }
}
