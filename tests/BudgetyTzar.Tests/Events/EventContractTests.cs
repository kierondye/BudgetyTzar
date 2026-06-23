using System.Text.Json;
using System.Text.Json.Nodes;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests;

public sealed class EventContractTests
{
    [Fact]
    public async Task RuntimeEventSchemaValidatorRejectsMalformedUnknownAndInvalidPayloadEvents()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using var scope = app.Services.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<EventSchemaValidator>();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var validEnvelopeJson = await db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.EventType == "budgetytzar.budgeting.budget-adjustment-recorded.v1")
            .Select(x => x.EnvelopeJson)
            .SingleAsync();

        Assert.NotNull(validator.ValidateAndDeserialize(validEnvelopeJson));
        Assert.Throws<PermanentProjectionException>(() => validator.ValidateAndDeserialize("{ invalid-json"));

        var unknown = JsonNode.Parse(validEnvelopeJson)!.AsObject();
        unknown["eventType"] = "budgetytzar.budgeting.unknown-event.v1";
        Assert.Throws<PermanentProjectionException>(() => validator.ValidateAndDeserialize(unknown.ToJsonString(EventSerialization.Options)));

        var invalidPayload = JsonNode.Parse(validEnvelopeJson)!.AsObject();
        invalidPayload["payload"]!.AsObject().Remove("amount");
        Assert.Throws<PermanentProjectionException>(() => validator.ValidateAndDeserialize(invalidPayload.ToJsonString(EventSerialization.Options)));
    }

    [Fact]
    public void EventSchemaSamplesContainRequiredDomainContractFields()
    {
        var root = FindRepoRoot();
        var envelopeSchema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/events/event-envelope.schema.json")));
        var reallocationSchema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/events/budgeting/budget-reallocation-recorded.v1.schema.json")));
        var manuallyCreatedSchema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/events/transactions/transaction-manually-created.v1.schema.json")));
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
              "auditEventType": "BudgetReallocationRecorded",
              "auditDescription": "Recorded budget reallocation.",
              "auditDetails": null,
              "budgetReallocationId": "33333333-3333-3333-3333-333333333333",
              "budgetId": "55555555-5555-5555-5555-555555555555",
              "date": "2026-06-12",
              "notes": "Move budget.",
              "adjustments": []
            }
            """);
        var manuallyCreatedPayload = JsonDocument.Parse("""
            {
              "auditEventId": "44444444-4444-4444-4444-444444444444",
              "auditEventType": "TransactionManuallyCreated",
              "auditDescription": "Created transaction Manual transaction for 12.50 Debit.",
              "auditDetails": null,
              "transactionId": "77777777-7777-7777-7777-777777777777",
              "budgetId": "55555555-5555-5555-5555-555555555555",
              "transactionDate": "2026-06-14",
              "description": "Manual transaction.",
              "amount": 12.50,
              "direction": "debit",
              "sourceAccount": "Current",
              "externalReference": "manual-001",
              "notes": null,
              "isIgnored": false
            }
            """);

        JsonSchemaTestAssertions.AssertRequiredProperties(envelopeSchema, envelope);
        JsonSchemaTestAssertions.AssertRequiredProperties(reallocationSchema, payload);
        JsonSchemaTestAssertions.AssertRequiredProperties(manuallyCreatedSchema, manuallyCreatedPayload);
    }

    [Fact]
    public async Task RealOutboxEnvelopesValidateAgainstDomainContractSchemas()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var savings = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Savings");
        var archived = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Old category");
        await BudgetApiTestClient.RecordReallocation(client, budget.Id, groceries.Id, savings.Id, 10m);
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, groceries.Id, 5m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 12));
        await BudgetApiTestClient.ArchiveBudgetItem(client, budget.Id, archived.Id);

        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 12), 35m, TransactionDirection.Debit);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, transaction.Id, [new TransactionAllocationItem(groceries.Id, 10m)]);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, transaction.Id, [
            new TransactionAllocationItem(groceries.Id, 10m),
            new TransactionAllocationItem(savings.Id, 15m)
        ]);
        await BudgetApiTestClient.ClearAllocations(client, budget.Id, transaction.Id);
        await BudgetApiTestClient.UpdateTransaction(client, budget.Id, transaction.Id);
        await BudgetApiTestClient.IgnoreTransaction(client, budget.Id, transaction.Id);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var outbox = (await db.OutboxMessages.AsNoTracking().ToListAsync())
            .OrderBy(x => x.CreatedAt)
            .ToList();
        var root = FindRepoRoot();
        using var envelopeSchema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/events/event-envelope.schema.json")));

        var expectedEventTypes = new[]
        {
            "budgetytzar.budgeting.budget-created.v1",
            "budgetytzar.budgeting.budget-item-created.v1",
            "budgetytzar.budgeting.budget-reallocation-recorded.v1",
            "budgetytzar.budgeting.budget-adjustment-recorded.v1",
            "budgetytzar.budgeting.budget-item-archived.v1",
            "budgetytzar.transactions.transaction-manually-created.v1",
            "budgetytzar.transactions.transaction-allocations-replaced.v1",
            "budgetytzar.transactions.transaction-allocations-cleared.v1",
            "budgetytzar.transactions.transaction-edited.v1",
            "budgetytzar.transactions.transaction-ignored.v1"
        };

        foreach (var eventType in expectedEventTypes)
        {
            Assert.Contains(outbox, x => x.EventType == eventType);
        }

        foreach (var message in outbox)
        {
            using var envelope = JsonDocument.Parse(message.EnvelopeJson);
            JsonSchemaTestAssertions.AssertElementMatchesSchema(envelopeSchema.RootElement, envelope.RootElement, message.EventType);

            var payloadSchemaPath = PayloadSchemaPath(root, message.EventType);
            using var payloadSchema = JsonDocument.Parse(File.ReadAllText(payloadSchemaPath));
            JsonSchemaTestAssertions.AssertElementMatchesSchema(payloadSchema.RootElement, envelope.RootElement.GetProperty("payload"), message.EventType);
        }
    }

    private static string PayloadSchemaPath(string root, string eventType)
    {
        var parts = eventType.Split('.');
        Assert.Equal("budgetytzar", parts[0]);
        return Path.Combine(root, "contracts/events", parts[1], $"{parts[2]}.{parts[3]}.schema.json");
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
}
