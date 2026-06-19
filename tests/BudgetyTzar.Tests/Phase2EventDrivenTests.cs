using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Application.Transactions;
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
    public async Task CommandsWriteAuditRecordsAndDomainShapedOutboxEnvelopesAtomically()
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
        Assert.Equal(budget.Name, envelope.Payload["name"]!.GetValue<string>());
        Assert.Equal(budget.Currency, envelope.Payload["currency"]!.GetValue<string>());
        Assert.Equal(audit.Id, envelope.Payload["auditEventId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task ProjectionRebuildFromOutboxSupportsProjectionBackedSnapshotsAndAuditTimeline()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetLine(client, budget.Id, "Salary");
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries");
        await RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));
        await RecordAdjustment(client, budget.Id, groceries.Id, 100m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 10));
        var transaction = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 12), 35m, TransactionDirection.Debit);
        await ReplaceAssignments(client, budget.Id, transaction.Id, [new TransactionAssignmentItem(groceries.Id, 35m)]);

        using (var scope = app.Services.CreateScope())
        {
            var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();
            await projector.RebuildFromOutbox(CancellationToken.None);
        }

        using var verifyScope = app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var snapshot = await db.BudgetSnapshotProjections.AsNoTracking().SingleAsync(x => x.BudgetId == budget.Id && x.Date == new DateOnly(2026, 6, 12));
        var item = await db.BudgetSnapshotItemProjections.AsNoTracking().SingleAsync(x => x.SnapshotId == snapshot.Id && x.BudgetItemId == groceries.Id);

        Assert.Equal(65m, item.Balance);
        Assert.Equal(-35m, snapshot.TotalBalance);
        Assert.True(await db.BudgetAuditTimelines.AnyAsync(x => x.BudgetId == budget.Id && x.EventType == "TransactionAssigned"));

        var apiSnapshot = await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budget.Id}/snapshot?date=2026-06-30");
        Assert.Equal(65m, apiSnapshot!.BudgetItems.Single(x => x.BudgetItemId == groceries.Id).Balance);
    }

    [Fact]
    public async Task ProjectionEnvelopeProcessingIsIdempotentForDuplicateEvents()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetLine(client, budget.Id, "Salary");
        await RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var envelopeJson = (await db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.EventType == "budgetytzar.budgeting.budget-adjustment-recorded.v1")
            .SingleAsync())
            .EnvelopeJson;
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();

        await projector.ProjectEnvelope(envelopeJson, CancellationToken.None);
        await projector.ProjectEnvelope(envelopeJson, CancellationToken.None);

        Assert.Equal(1, await db.ProcessedProjectionEvents.CountAsync());
        Assert.Equal(1, await db.BudgetSnapshotProjections.CountAsync(x => x.BudgetId == budget.Id && x.Date == new DateOnly(2026, 6, 10)));
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

        AssertRequiredProperties(envelopeSchema, envelope);
        AssertRequiredProperties(reallocationSchema, payload);
        AssertRequiredProperties(manuallyCreatedSchema, manuallyCreatedPayload);
    }

    [Fact]
    public async Task RealOutboxEnvelopesValidateAgainstDomainContractSchemas()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries");
        var savings = await CreateBudgetLine(client, budget.Id, "Savings");
        var archived = await CreateBudgetLine(client, budget.Id, "Old category");
        await RecordReallocation(client, budget.Id, groceries.Id, savings.Id, 10m);
        await RecordAdjustment(client, budget.Id, groceries.Id, 5m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 12));
        await ArchiveBudgetLine(client, budget.Id, archived.Id);

        var transaction = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 12), 35m, TransactionDirection.Debit);
        await ReplaceAssignments(client, budget.Id, transaction.Id, [new TransactionAssignmentItem(groceries.Id, 10m)]);
        await ReplaceAssignments(client, budget.Id, transaction.Id, [
            new TransactionAssignmentItem(groceries.Id, 10m),
            new TransactionAssignmentItem(savings.Id, 15m)
        ]);
        await ClearAssignments(client, budget.Id, transaction.Id);
        await UpdateTransaction(client, budget.Id, transaction.Id);
        await IgnoreTransaction(client, budget.Id, transaction.Id);

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
            "budgetytzar.budgeting.budget-line-created.v1",
            "budgetytzar.budgeting.budget-reallocation-recorded.v1",
            "budgetytzar.budgeting.budget-adjustment-recorded.v1",
            "budgetytzar.budgeting.budget-line-archived.v1",
            "budgetytzar.transactions.transaction-manually-created.v1",
            "budgetytzar.transactions.transaction-assigned.v1",
            "budgetytzar.transactions.transaction-split.v1",
            "budgetytzar.transactions.transaction-assignments-cleared.v1",
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
            AssertElementMatchesSchema(envelopeSchema.RootElement, envelope.RootElement, message.EventType);

            var payloadSchemaPath = PayloadSchemaPath(root, message.EventType);
            using var payloadSchema = JsonDocument.Parse(File.ReadAllText(payloadSchemaPath));
            AssertElementMatchesSchema(payloadSchema.RootElement, envelope.RootElement.GetProperty("payload"), message.EventType);
        }
    }

    private static void AssertRequiredProperties(JsonDocument schema, JsonDocument sample)
    {
        foreach (var property in schema.RootElement.GetProperty("required").EnumerateArray())
        {
            Assert.True(sample.RootElement.TryGetProperty(property.GetString()!, out _), $"Missing required property {property.GetString()}.");
        }
    }

    private static void AssertElementMatchesSchema(JsonElement schema, JsonElement element, string context)
    {
        foreach (var property in schema.GetProperty("required").EnumerateArray())
        {
            Assert.True(element.TryGetProperty(property.GetString()!, out _), $"{context}: missing required property {property.GetString()}.");
        }

        var properties = schema.GetProperty("properties");
        if (schema.TryGetProperty("additionalProperties", out var additionalProperties)
            && additionalProperties.ValueKind == JsonValueKind.False)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.True(properties.TryGetProperty(property.Name, out _), $"{context}: unexpected property {property.Name}.");
            }
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (!element.TryGetProperty(property.Name, out var value))
            {
                continue;
            }

            if (property.Value.TryGetProperty("const", out var constValue))
            {
                Assert.Equal(constValue.GetString(), value.GetString());
            }

            if (property.Value.TryGetProperty("type", out var type))
            {
                AssertSchemaType(type, value, $"{context}.{property.Name}");
            }

            if (property.Value.TryGetProperty("pattern", out var pattern))
            {
                Assert.Matches(new Regex(pattern.GetString()!), value.GetString()!);
            }
        }
    }

    private static void AssertSchemaType(JsonElement type, JsonElement value, string context)
    {
        var allowedTypes = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(x => x.GetString()!).ToArray()
            : [type.GetString()!];

        var actualType = value.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.Null => "null",
            _ => value.ValueKind.ToString()
        };

        Assert.True(allowedTypes.Contains(actualType) || actualType == "number" && allowedTypes.Contains("integer"), $"{context}: expected {string.Join(" or ", allowedTypes)}, got {actualType}.");
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

    private static async Task<Budget> CreateBudget(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Budget>())!;
    }

    private static async Task<BudgetItemDto> CreateBudgetLine(HttpClient client, Guid budgetId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items",
            new CreateBudgetItemRequest(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetItemDto>())!;
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
            $"/api/budgets/{budgetId}/transactions/{transactionId}/allocations",
            new ReplaceTransactionAllocationsRequest(assignments));
        response.EnsureSuccessStatusCode();
    }

    private static async Task ClearAssignments(HttpClient client, Guid budgetId, Guid transactionId)
    {
        var response = await client.DeleteAsync($"/api/budgets/{budgetId}/transactions/{transactionId}/allocations");
        response.EnsureSuccessStatusCode();
    }

    private static async Task UpdateTransaction(HttpClient client, Guid budgetId, Guid transactionId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions/{transactionId}",
            new UpdateTransactionRequest(new DateOnly(2026, 6, 13), "Groceries updated", 35m, TransactionDirection.Debit, "Current", null, null));
        response.EnsureSuccessStatusCode();
    }

    private static async Task IgnoreTransaction(HttpClient client, Guid budgetId, Guid transactionId)
    {
        var response = await client.PostAsync($"/api/budgets/{budgetId}/transactions/{transactionId}/ignore", null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task RecordAdjustment(HttpClient client, Guid budgetId, Guid budgetLineId, decimal amount, BudgetAdjustmentType type, DateOnly date)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items/{budgetLineId}/adjustments",
            new CreateBudgetItemAdjustmentRequest(amount, type, date, "Projection test adjustment"));
        response.EnsureSuccessStatusCode();
    }

    private static async Task RecordReallocation(HttpClient client, Guid budgetId, Guid fromBudgetLineId, Guid toBudgetLineId, decimal amount)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/reallocations",
            new CreateBudgetItemReallocationRequest(
                new DateOnly(2026, 6, 12),
                "Schema validation reallocation",
                [
                    new BudgetReallocationAdjustmentItem(fromBudgetLineId, amount, BudgetAdjustmentType.Credit),
                    new BudgetReallocationAdjustmentItem(toBudgetLineId, amount, BudgetAdjustmentType.Debit)
                ]));
        response.EnsureSuccessStatusCode();
    }

    private static async Task ArchiveBudgetLine(HttpClient client, Guid budgetId, Guid budgetLineId)
    {
        var response = await client.PostAsync($"/api/budgets/{budgetId}/budget-items/{budgetLineId}/archive", null);
        response.EnsureSuccessStatusCode();
    }

}
