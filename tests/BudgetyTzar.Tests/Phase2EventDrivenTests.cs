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
    public async Task ProjectionEnvelopeProcessingIsIdempotentForDuplicateEvents()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        await ReplaceAllocations(client, budget.Id, period.Id, [new BudgetLineAllocationItem(groceries.Id, 100m)]);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var envelopeJson = (await db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.BudgetId == budget.Id)
            .ToListAsync())
            .OrderByDescending(x => x.CreatedAt)
            .First()
            .EnvelopeJson;
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();

        await projector.ProjectEnvelope(envelopeJson, CancellationToken.None);
        await projector.ProjectEnvelope(envelopeJson, CancellationToken.None);

        Assert.Equal(1, await db.ProcessedProjectionEvents.CountAsync());
        Assert.Equal(1, await db.PeriodBudgetSummaries.CountAsync(x => x.BudgetPeriodId == period.Id));
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

    [Fact]
    public async Task RealOutboxEnvelopesValidateAgainstCanonicalSchemas()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var savings = await CreateBudgetLine(client, budget.Id, "Savings", BudgetLineDirection.Debit);
        var archived = await CreateBudgetLine(client, budget.Id, "Old category", BudgetLineDirection.Debit);
        await ReplaceAllocations(client, budget.Id, period.Id, [
            new BudgetLineAllocationItem(groceries.Id, 100m),
            new BudgetLineAllocationItem(savings.Id, 25m)
        ]);
        await RecordReallocation(client, budget.Id, period.Id, groceries.Id, savings.Id, 10m);
        await RecordAdjustment(client, budget.Id, period.Id, groceries.Id, 5m);
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

        var import = await PreviewImport(client, budget.Id);
        await CommitImport(client, budget.Id, import.Batch.Id);

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
            "budgetytzar.budgeting.budget-period-created.v1",
            "budgetytzar.budgeting.budget-line-created.v1",
            "budgetytzar.budgeting.budget-line-allocations-replaced.v1",
            "budgetytzar.budgeting.budget-reallocation-recorded.v1",
            "budgetytzar.budgeting.budget-adjustment-recorded.v1",
            "budgetytzar.budgeting.budget-line-archived.v1",
            "budgetytzar.transactions.transaction-manually-created.v1",
            "budgetytzar.transactions.transaction-assigned.v1",
            "budgetytzar.transactions.transaction-split.v1",
            "budgetytzar.transactions.transaction-assignments-cleared.v1",
            "budgetytzar.transactions.transaction-edited.v1",
            "budgetytzar.transactions.transaction-ignored.v1",
            "budgetytzar.transactions.transaction-import-batch-previewed.v1",
            "budgetytzar.transactions.transaction-imported.v1",
            "budgetytzar.transactions.transaction-import-batch-committed.v1"
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
            JsonValueKind.Number => "integer",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.Null => "null",
            _ => value.ValueKind.ToString()
        };

        Assert.True(allowedTypes.Contains(actualType), $"{context}: expected {string.Join(" or ", allowedTypes)}, got {actualType}.");
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

    private static async Task ClearAssignments(HttpClient client, Guid budgetId, Guid transactionId)
    {
        var response = await client.DeleteAsync($"/api/budgets/{budgetId}/transactions/{transactionId}/assignments");
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

    private static async Task RecordAdjustment(HttpClient client, Guid budgetId, Guid periodId, Guid budgetLineId, decimal amount)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/periods/{periodId}/adjustments",
            new CreateBudgetAdjustmentRequest(budgetLineId, amount, "Projection test adjustment"));
        response.EnsureSuccessStatusCode();
    }

    private static async Task RecordReallocation(HttpClient client, Guid budgetId, Guid periodId, Guid fromBudgetLineId, Guid toBudgetLineId, decimal amount)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/periods/{periodId}/reallocations",
            new CreateBudgetReallocationRequest(fromBudgetLineId, toBudgetLineId, amount, "Schema validation reallocation"));
        response.EnsureSuccessStatusCode();
    }

    private static async Task ArchiveBudgetLine(HttpClient client, Guid budgetId, Guid budgetLineId)
    {
        var response = await client.PostAsync($"/api/budgets/{budgetId}/budget-lines/{budgetLineId}/archive", null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<TransactionImportDetail> PreviewImport(HttpClient client, Guid budgetId)
    {
        var csv = """
            date,description,amount,direction,source account,external reference,notes
            2026-06-14,Imported lunch,12.50,Debit,Current,import-001,
            """;
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/transaction-imports/preview",
            new PreviewTransactionImportRequest("schema-import.csv", csv));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionImportDetail>())!;
    }

    private static async Task CommitImport(HttpClient client, Guid budgetId, Guid batchId)
    {
        var response = await client.PostAsync($"/api/budgets/{budgetId}/transaction-imports/{batchId}/commit", null);
        response.EnsureSuccessStatusCode();
    }
}
