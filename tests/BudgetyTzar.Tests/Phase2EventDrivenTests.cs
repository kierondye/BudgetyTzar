using System.Net.Http.Json;
using System.Text.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BudgetyTzar.Tests;

public sealed class Phase2EventDrivenTests
{
    [Fact]
    public async Task CommandsWriteAuditRecordsAndOutboxEnvelopesAtomically()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var budget = await CreateBudget(client);

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
        Assert.Equal(audit.Id, envelope.Payload["auditEventId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task ProjectionRebuildFromOutboxSupportsProjectionBackedPeriodSummary()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        await ReplaceAllocations(client, budget.Id, period.Id, [new BudgetLineAllocationItem(groceries.Id, 100m)]);
        var transaction = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 12), 35m, TransactionDirection.Debit);
        await ReplaceAssignments(client, budget.Id, transaction.Id, [new TransactionAssignmentItem(groceries.Id, 35m)]);
        await RecordAdjustment(client, budget.Id, period.Id, groceries.Id, 5m);

        using (var scope = app.Services.CreateScope())
        {
            var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();
            await projector.RebuildFromOutbox(CancellationToken.None);
        }

        var summary = await client.GetFromJsonAsync<PeriodSummary>(
            $"/api/budgets/{budget.Id}/reports/period-summary?periodId={period.Id}");

        Assert.NotNull(summary);
        Assert.Equal(100m, summary!.PlannedDebit);
        Assert.Equal(35m, summary.ActualDebit);
        Assert.Equal(70m, summary.DebitRemaining);
        Assert.Single(summary.Lines);
        Assert.Equal(5m, summary.Lines[0].AdjustmentAmount);

        using var verifyScope = app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        Assert.True(await db.PeriodBudgetSummaries.AnyAsync(x => x.BudgetPeriodId == period.Id));
        Assert.True(await db.BudgetLinePeriodSummaries.AnyAsync(x => x.BudgetLineId == groceries.Id));
        Assert.True(await db.TransactionAssignmentSummaries.AnyAsync(x => x.TransactionId == transaction.Id));
    }

    [Fact]
    public void EventSchemaSamplesContainRequiredContractFields()
    {
        var root = FindRepoRoot();
        var envelopeSchema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/events/event-envelope.schema.json")));
        var reallocationSchema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/events/budgeting/budget-reallocation-recorded.v1.schema.json")));
        var importedSchema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/events/transactions/transaction-imported.v1.schema.json")));
        var envelope = JsonDocument.Parse("""
            {
              "eventId": "11111111-1111-1111-1111-111111111111",
              "eventType": "budgetytzar.budgeting.budget-reallocation-recorded.v1",
              "occurredAt": "2026-06-15T10:30:00Z",
              "correlationId": "22222222-2222-2222-2222-222222222222",
              "causationId": null,
              "aggregateId": "33333333-3333-3333-3333-333333333333",
              "aggregateType": "BudgetReallocation",
              "schemaVersion": 1,
              "payload": {}
            }
            """);
        var payload = JsonDocument.Parse("""
            {
              "auditEventId": "44444444-4444-4444-4444-444444444444",
              "budgetId": "55555555-5555-5555-5555-555555555555",
              "budgetPeriodId": "66666666-6666-6666-6666-666666666666",
              "entityType": "BudgetReallocation",
              "entityId": "33333333-3333-3333-3333-333333333333",
              "eventName": "BudgetReallocationRecorded",
              "description": "Reallocated 30.00.",
              "details": null,
              "appliesToAllPeriods": false
            }
            """);
        var importedPayload = JsonDocument.Parse("""
            {
              "auditEventId": "44444444-4444-4444-4444-444444444444",
              "budgetId": "55555555-5555-5555-5555-555555555555",
              "budgetPeriodId": null,
              "entityType": "FinancialTransaction",
              "entityId": "77777777-7777-7777-7777-777777777777",
              "eventName": "TransactionImported",
              "description": "Imported transaction.",
              "details": null,
              "appliesToAllPeriods": false
            }
            """);

        AssertRequiredProperties(envelopeSchema, envelope);
        AssertRequiredProperties(reallocationSchema, payload);
        AssertRequiredProperties(importedSchema, importedPayload);
    }

    private static void AssertRequiredProperties(JsonDocument schema, JsonDocument sample)
    {
        foreach (var property in schema.RootElement.GetProperty("required").EnumerateArray())
        {
            Assert.True(sample.RootElement.TryGetProperty(property.GetString()!, out _), $"Missing required property {property.GetString()}.");
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BudgetyTzar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repo root.");
    }

    private static async Task<Budget> CreateBudget(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Budget>())!;
    }

    private static async Task<BudgetPeriod> CreatePeriod(HttpClient client, Guid budgetId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/periods",
            new CreateBudgetPeriodRequest("June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetPeriod>())!;
    }

    private static async Task<BudgetLine> CreateBudgetLine(HttpClient client, Guid budgetId, string name, BudgetLineDirection direction)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-lines",
            new CreateBudgetLineRequest(name, direction, BudgetLineRolloverType.PeriodReset));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetLine>())!;
    }

    private static async Task ReplaceAllocations(HttpClient client, Guid budgetId, Guid periodId, IReadOnlyList<BudgetLineAllocationItem> allocations)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budgetId}/periods/{periodId}/allocations",
            new ReplaceBudgetLineAllocationsRequest(allocations));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<FinancialTransaction> CreateTransaction(HttpClient client, Guid budgetId, DateOnly date, decimal amount, TransactionDirection direction)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions",
            new CreateTransactionRequest(date, "Groceries", amount, direction, "Current", null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FinancialTransaction>())!;
    }

    private static async Task ReplaceAssignments(HttpClient client, Guid budgetId, Guid transactionId, IReadOnlyList<TransactionAssignmentItem> assignments)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions/{transactionId}/assignments",
            new ReplaceTransactionAssignmentsRequest(assignments));
        response.EnsureSuccessStatusCode();
    }

    private static async Task RecordAdjustment(HttpClient client, Guid budgetId, Guid periodId, Guid budgetLineId, decimal amount)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/periods/{periodId}/adjustments",
            new CreateBudgetAdjustmentRequest(budgetLineId, amount, "Projection test adjustment"));
        response.EnsureSuccessStatusCode();
    }
}
