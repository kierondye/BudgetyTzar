using System.Net.Http.Json;
using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Application.Transactions;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Confluent.Kafka;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        Assert.Equal(audit.EventType, envelope.Payload["auditEventType"]!.GetValue<string>());
        Assert.Equal(audit.Description, envelope.Payload["auditDescription"]!.GetValue<string>());
        Assert.Null(envelope.Payload["auditDetails"]?.GetValue<string>());
    }

    [Fact]
    public async Task CommandResponsesExposeProjectionReadinessHeaders()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        response.EnsureSuccessStatusCode();
        var budget = (await response.Content.ReadFromJsonAsync<Budget>())!;
        var eventId = Assert.Single(response.Headers.GetValues(CommandResultHttpExtensions.EventIdHeaderName));
        var statusUrl = Assert.Single(response.Headers.GetValues(CommandResultHttpExtensions.ProjectionStatusHeaderName));

        Assert.True(Guid.TryParse(eventId, out _));
        Assert.Equal($"/api/budgets/{budget.Id}/projections/status?eventId={eventId}", statusUrl);
    }

    [Fact]
    public async Task ProjectionRebuildFromOutboxSupportsProjectionBackedSnapshotsAndAuditTimeline()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
        var groceries = await CreateBudgetItem(client, budget.Id, "Groceries");
        await RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));
        await RecordAdjustment(client, budget.Id, groceries.Id, 100m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 10));
        var transaction = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 12), 35m, TransactionDirection.Debit);
        await ReplaceAllocations(client, budget.Id, transaction.Id, [new TransactionAllocationItem(groceries.Id, 35m)]);

        using (var scope = app.Services.CreateScope())
        {
            var purgeDb = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            await purgeDb.TransactionAllocations.ExecuteDeleteAsync();
            await purgeDb.Transactions.ExecuteDeleteAsync();
            await purgeDb.BudgetAdjustments.ExecuteDeleteAsync();
            await purgeDb.BudgetItems.ExecuteDeleteAsync();
            await purgeDb.AuditEvents.ExecuteDeleteAsync();

            var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();
            await projector.RebuildFromOutbox(CancellationToken.None);
        }

        using var verifyScope = app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var snapshot = await db.BudgetSnapshotProjections.AsNoTracking().SingleAsync(x => x.BudgetId == budget.Id && x.Date == new DateOnly(2026, 6, 12));
        var item = await db.BudgetSnapshotItemProjections.AsNoTracking().SingleAsync(x => x.SnapshotId == snapshot.Id && x.BudgetItemId == groceries.Id);

        Assert.Equal(65m, item.Balance);
        Assert.Equal(-35m, snapshot.TotalBalance);
        Assert.True(await db.BudgetAuditTimelines.AnyAsync(x => x.BudgetId == budget.Id && x.EventType == "TransactionAllocationsReplaced"));

        var apiSnapshot = await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budget.Id}/snapshot?date=2026-06-30");
        Assert.Equal(65m, apiSnapshot!.BudgetItems.Single(x => x.BudgetItemId == groceries.Id).Balance);

        var apiAuditEvents = await client.GetFromJsonAsync<IReadOnlyList<AuditEventDto>>(
            $"/api/budgets/{budget.Id}/audit-events");
        Assert.Contains(apiAuditEvents!, x => x.EventType == "TransactionAllocationsReplaced");
    }

    [Fact]
    public async Task AllocationReplacementWorkflowsEmitReplacedAndDeleteEmitsCleared()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetItem(client, budget.Id, "Groceries");
        var savings = await CreateBudgetItem(client, budget.Id, "Savings");
        var household = await CreateBudgetItem(client, budget.Id, "Household");
        var single = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 12), 35m, TransactionDirection.Debit);
        var multi = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 13), 40m, TransactionDirection.Debit);
        var manyToOne = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 14), 50m, TransactionDirection.Debit);
        var empty = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 15), 20m, TransactionDirection.Debit);

        await ReplaceAllocations(client, budget.Id, single.Id, [new TransactionAllocationItem(groceries.Id, 35m)]);
        await ReplaceAllocations(client, budget.Id, multi.Id, [
            new TransactionAllocationItem(groceries.Id, 15m),
            new TransactionAllocationItem(savings.Id, 25m)
        ]);
        await ReplaceAllocations(client, budget.Id, manyToOne.Id, [
            new TransactionAllocationItem(groceries.Id, 20m),
            new TransactionAllocationItem(savings.Id, 20m)
        ]);
        await ReplaceAllocations(client, budget.Id, manyToOne.Id, [new TransactionAllocationItem(household.Id, 50m)]);
        await ReplaceAllocations(client, budget.Id, empty.Id, []);
        await ClearAllocations(client, budget.Id, multi.Id);

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
        var budget = await CreateBudget(client);

        var apiAuditEvents = await client.GetFromJsonAsync<IReadOnlyList<AuditEventDto>>(
            $"/api/budgets/{budget.Id}/audit-events");

        Assert.Contains(apiAuditEvents!, x => x.EventType == "BudgetCreated");
    }

    [Fact]
    public async Task ProjectionEnvelopeProcessingIsIdempotentForDuplicateEvents()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
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
    public async Task ProjectionEnvelopeProcessingMarksOnlyTheConsumedOutboxMessageProjected()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
        await RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var consumedMessage = await db.OutboxMessages
            .AsNoTracking()
            .SingleAsync(x => x.EventType == "budgetytzar.budgeting.budget-adjustment-recorded.v1");
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();

        await projector.ProjectEnvelope(consumedMessage.EnvelopeJson, CancellationToken.None);

        var outbox = await db.OutboxMessages.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync();
        Assert.NotNull(outbox.Single(x => x.Id == consumedMessage.Id).ProjectedAt);
        Assert.All(outbox.Where(x => x.Id != consumedMessage.Id), x => Assert.Null(x.ProjectedAt));
    }

    [Fact]
    public async Task DeltaProjectionProcessingUpdatesProjectionStateWithoutImplicitOutboxReplay()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
        await RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var messages = (await db.OutboxMessages.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync())
            .OrderBy(x => x.CreatedAt)
            .ToList();
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();

        foreach (var message in messages)
        {
            await projector.ProjectEnvelope(message.EnvelopeJson, CancellationToken.None);
        }

        Assert.Equal(messages.Count, await db.ProcessedProjectionEvents.CountAsync(x => x.BudgetId == budget.Id));
        Assert.Single(await db.BudgetItemProjectionStates.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync());
        Assert.Single(await db.BudgetAdjustmentProjectionStates.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync());
        Assert.True(await db.BudgetSnapshotItemProjections.AnyAsync(x => x.BudgetId == budget.Id && x.BudgetItemId == salary.Id && x.Balance == -100m));
    }

    [Fact]
    public async Task ProjectionBackedSnapshotEndpointDoesNotRebuildFromOutboxAtReadTime()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
        await RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using (var scope = app.Services.CreateScope())
        {
            var purgeDb = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            await purgeDb.BudgetAdjustments.ExecuteDeleteAsync();
            await purgeDb.BudgetItems.ExecuteDeleteAsync();
            await purgeDb.AuditEvents.ExecuteDeleteAsync();
        }

        var response = await client.GetAsync($"/api/budgets/{budget.Id}/snapshot?date=2026-06-10");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var verifyScope = app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        Assert.False(await db.BudgetSnapshotProjections.AnyAsync(x => x.BudgetId == budget.Id));
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.BudgetId == budget.Id && x.ProjectedAt != null));
    }

    [Fact]
    public async Task ProjectionBackedSnapshotReturnsAcceptedWhenProjectionRowsAreMissing()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);

        var response = await client.GetAsync($"/api/budgets/{budget.Id}/snapshot?date=2026-06-30");
        var pending = await response.Content.ReadFromJsonAsync<ProjectionPendingResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("pending", pending!.Status);
        Assert.Equal($"/api/budgets/{budget.Id}/projection-events?eventId={pending.EventId}", pending.EventStreamUrl);
    }

    [Fact]
    public async Task ProjectionEventStreamForPendingEventCompletesWhenMatchingNotificationArrives()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        response.EnsureSuccessStatusCode();
        var budget = (await response.Content.ReadFromJsonAsync<Budget>())!;
        var eventId = Guid.Parse(Assert.Single(response.Headers.GetValues(CommandResultHttpExtensions.EventIdHeaderName)));

        var streamTask = client.GetStringAsync($"/api/budgets/{budget.Id}/projection-events?eventId={eventId}");
        var notifications = app.Services.GetRequiredService<ProjectionNotificationService>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!streamTask.IsCompleted && DateTimeOffset.UtcNow < deadline)
        {
            notifications.Publish(new ProjectionReadyNotification(
                budget.Id,
                eventId,
                "budgetytzar.budgeting.budget-created.v1",
                DateTimeOffset.UtcNow,
                ["snapshot", "auditTimeline"]));
            await Task.Delay(50);
        }

        var body = await streamTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("event: projection-ready", body);
        Assert.Contains(eventId.ToString(), body);
    }

    [Fact]
    public async Task ProjectionBackedSnapshotReturnsZeroBalancesForBudgetItemsWithoutActivity()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetItem(client, budget.Id, "Groceries");

        using (var scope = app.Services.CreateScope())
        {
            var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();
            await projector.RebuildFromOutbox(CancellationToken.None);
        }

        var snapshot = await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budget.Id}/snapshot?date=2026-06-30");

        var item = Assert.Single(snapshot!.BudgetItems);
        Assert.Equal(groceries.Id, item.BudgetItemId);
        Assert.Equal(0m, item.Balance);
    }

    [Fact]
    public async Task ProjectionBackedAuditEndpointDoesNotRebuildFromOutboxAtReadTime()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);

        var response = await client.GetAsync($"/api/budgets/{budget.Id}/audit-events");
        var pending = await response.Content.ReadFromJsonAsync<ProjectionPendingResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("pending", pending!.Status);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        Assert.False(await db.BudgetAuditTimelines.AnyAsync(x => x.BudgetId == budget.Id));
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.BudgetId == budget.Id && x.ProjectedAt != null));
    }

    [Fact]
    public async Task ProjectionStatusTransitionsFromPendingToReady()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var createResponse = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        createResponse.EnsureSuccessStatusCode();
        var budget = (await createResponse.Content.ReadFromJsonAsync<Budget>())!;
        var eventId = Guid.Parse(Assert.Single(createResponse.Headers.GetValues(CommandResultHttpExtensions.EventIdHeaderName)));

        var pending = await client.GetFromJsonAsync<ProjectionStatusResponse>(
            $"/api/budgets/{budget.Id}/projections/status?eventId={eventId}");
        Assert.Equal("pending", pending!.Status);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();
            var envelopeJson = await db.OutboxMessages
                .Where(x => x.Id == eventId)
                .Select(x => x.EnvelopeJson)
                .SingleAsync();
            await projector.ProjectEnvelope(envelopeJson, CancellationToken.None);
        }

        var ready = await client.GetFromJsonAsync<ProjectionStatusResponse>(
            $"/api/budgets/{budget.Id}/projections/status?eventId={eventId}");
        var unknown = await client.GetFromJsonAsync<ProjectionStatusResponse>(
            $"/api/budgets/{Guid.NewGuid()}/projections/status?eventId={eventId}");

        Assert.Equal("ready", ready!.Status);
        Assert.Equal("unknown", unknown!.Status);
    }

    [Fact]
    public async Task ReportingProjectionConsumerProjectsEventsConsumedFromKafka()
    {
        var kafkaPort = GetFreeTcpPort();
        var bootstrapServers = $"127.0.0.1:{kafkaPort}";
        await using var kafka = new ContainerBuilder("docker.redpanda.com/redpandadata/redpanda:v24.3.7")
            .WithPortBinding(kafkaPort, 19092)
            .WithCommand(
                "redpanda",
                "start",
                "--mode", "dev-container",
                "--smp", "1",
                "--memory", "512M",
                "--overprovisioned",
                "--node-id", "0",
                "--check=false",
                "--kafka-addr", "internal://0.0.0.0:9092,external://0.0.0.0:19092",
                "--advertise-kafka-addr", $"internal://127.0.0.1:9092,external://127.0.0.1:{kafkaPort}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(19092))
            .Build();
        await kafka.StartAsync();

        await using var app = new KafkaBudgetApiFactory(bootstrapServers);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
        await RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            var messages = (await db.OutboxMessages.AsNoTracking().ToListAsync())
                .OrderBy(x => x.CreatedAt)
                .ToList();
            using var producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = bootstrapServers
            }).Build();

            foreach (var message in messages)
            {
                await producer.ProduceAsync(
                    message.Topic,
                    new Message<string, string>
                    {
                        Key = message.AggregateId.ToString(),
                        Value = message.EnvelopeJson
                    });
            }

            producer.Flush(TimeSpan.FromSeconds(10));
        }

        await WaitUntil(async () =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            return await db.BudgetSnapshotItemProjections.AnyAsync(x => x.BudgetId == budget.Id && x.BudgetItemId == salary.Id && x.Balance == -100m)
                && await db.BudgetAuditTimelines.AnyAsync(x => x.BudgetId == budget.Id && x.EventType == "BudgetAdjustmentRecorded");
        });
    }

    [Fact]
    public async Task ReportingProjectionConsumerDeadLettersPoisonEventAndContinuesWithLaterEvents()
    {
        var kafkaPort = GetFreeTcpPort();
        var bootstrapServers = $"127.0.0.1:{kafkaPort}";
        await using var kafka = new ContainerBuilder("docker.redpanda.com/redpandadata/redpanda:v24.3.7")
            .WithPortBinding(kafkaPort, 19092)
            .WithCommand(
                "redpanda",
                "start",
                "--mode", "dev-container",
                "--smp", "1",
                "--memory", "512M",
                "--overprovisioned",
                "--node-id", "0",
                "--check=false",
                "--kafka-addr", "internal://0.0.0.0:9092,external://0.0.0.0:19092",
                "--advertise-kafka-addr", $"internal://127.0.0.1:9092,external://127.0.0.1:{kafkaPort}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(19092))
            .Build();
        await kafka.StartAsync();

        await using var app = new KafkaBudgetApiFactory(bootstrapServers);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
        await RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            var messages = (await db.OutboxMessages.AsNoTracking().ToListAsync())
                .OrderBy(x => x.CreatedAt)
                .ToList();
            using var producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = bootstrapServers
            }).Build();

            await producer.ProduceAsync(
                "budgetytzar.budgeting.events",
                new Message<string, string>
                {
                    Key = Guid.NewGuid().ToString(),
                    Value = "{ invalid-json"
                });

            foreach (var message in messages)
            {
                await producer.ProduceAsync(
                    message.Topic,
                    new Message<string, string>
                    {
                        Key = message.AggregateId.ToString(),
                        Value = message.EnvelopeJson
                    });
            }

            producer.Flush(TimeSpan.FromSeconds(10));
        }

        await WaitUntil(async () =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            return await db.ProjectionEventFailures.AnyAsync(x => x.Status == ProjectionFailureStatus.DeadLettered)
                && await db.BudgetSnapshotItemProjections.AnyAsync(x => x.BudgetId == budget.Id && x.BudgetItemId == salary.Id && x.Balance == -100m);
        });
    }

    [Fact]
    public async Task RuntimeEventSchemaValidatorRejectsMalformedUnknownAndInvalidPayloadEvents()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
        await RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

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
        var groceries = await CreateBudgetItem(client, budget.Id, "Groceries");
        var savings = await CreateBudgetItem(client, budget.Id, "Savings");
        var archived = await CreateBudgetItem(client, budget.Id, "Old category");
        await RecordReallocation(client, budget.Id, groceries.Id, savings.Id, 10m);
        await RecordAdjustment(client, budget.Id, groceries.Id, 5m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 12));
        await ArchiveBudgetItem(client, budget.Id, archived.Id);

        var transaction = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 12), 35m, TransactionDirection.Debit);
        await ReplaceAllocations(client, budget.Id, transaction.Id, [new TransactionAllocationItem(groceries.Id, 10m)]);
        await ReplaceAllocations(client, budget.Id, transaction.Id, [
            new TransactionAllocationItem(groceries.Id, 10m),
            new TransactionAllocationItem(savings.Id, 15m)
        ]);
        await ClearAllocations(client, budget.Id, transaction.Id);
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

            if (property.Value.TryGetProperty("items", out var items) && value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    AssertElementMatchesSchema(items, item, $"{context}.{property.Name}[{index}]");
                    index++;
                }
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

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail("Condition was not met before the timeout.");
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<Budget> CreateBudget(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Budget>())!;
    }

    private static async Task<BudgetItemDto> CreateBudgetItem(HttpClient client, Guid budgetId, string name)
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

    private static async Task ReplaceAllocations(HttpClient client, Guid budgetId, Guid transactionId, IReadOnlyList<TransactionAllocationItem> allocations)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions/{transactionId}/allocations",
            new ReplaceTransactionAllocationsRequest(allocations));
        response.EnsureSuccessStatusCode();
    }

    private static async Task ClearAllocations(HttpClient client, Guid budgetId, Guid transactionId)
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

    private static async Task RecordAdjustment(HttpClient client, Guid budgetId, Guid budgetItemId, decimal amount, BudgetAdjustmentType type, DateOnly date)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments",
            new CreateBudgetItemAdjustmentRequest(amount, type, date, "Projection test adjustment"));
        response.EnsureSuccessStatusCode();
    }

    private static async Task RecordReallocation(HttpClient client, Guid budgetId, Guid fromBudgetItemId, Guid toBudgetItemId, decimal amount)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/reallocations",
            new CreateBudgetItemReallocationRequest(
                new DateOnly(2026, 6, 12),
                "Schema validation reallocation",
                [
                    new BudgetReallocationAdjustmentItem(fromBudgetItemId, amount, BudgetAdjustmentType.Credit),
                    new BudgetReallocationAdjustmentItem(toBudgetItemId, amount, BudgetAdjustmentType.Debit)
                ]));
        response.EnsureSuccessStatusCode();
    }

    private static async Task ArchiveBudgetItem(HttpClient client, Guid budgetId, Guid budgetItemId)
    {
        var response = await client.PostAsync($"/api/budgets/{budgetId}/budget-items/{budgetItemId}/archive", null);
        response.EnsureSuccessStatusCode();
    }

}

internal sealed class KafkaBudgetApiFactory(string bootstrapServers) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Kafka:BootstrapServers", bootstrapServers);
        builder.UseSetting("Outbox:PublisherEnabled", "false");
        builder.UseSetting("Projections:ConsumerEnabled", "true");
        builder.UseSetting("Projections:UseProjectionBackedReports", "true");
        builder.UseSetting("Projections:MaxRetryAttempts", "1");
        builder.UseSetting("Projections:InitialRetryDelayMilliseconds", "10");
        builder.UseSetting("Projections:MaxRetryDelayMilliseconds", "10");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<BudgetDbContext>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<BudgetDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BudgetDbContext>>();
            services.RemoveAll<DbConnection>();

            services.AddSingleton<DbConnection>(_ =>
            {
                var connection = new SqliteConnection("DataSource=:memory:");
                connection.Open();
                return connection;
            });

            services.AddDbContext<BudgetDbContext>((provider, options) =>
                options.UseSqlite(provider.GetRequiredService<DbConnection>()));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }
}
