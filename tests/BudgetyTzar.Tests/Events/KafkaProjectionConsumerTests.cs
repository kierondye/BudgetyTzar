using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests;

[Collection(KafkaTestCollection.Name)]
public sealed class KafkaProjectionConsumerTests
{
    [Fact]
    public async Task ReportingProjectionConsumerProjectsEventsConsumedFromKafka()
    {
        var kafkaPort = ProjectionTestHelpers.GetFreeTcpPort();
        var bootstrapServers = $"127.0.0.1:{kafkaPort}";
        await using var kafka = await ProjectionTestHelpers.StartRedpandaAsync(kafkaPort);
        await ProjectionTestHelpers.CreateKafkaTopicsAsync(bootstrapServers);

        await using var app = new KafkaBudgetApiFactory(bootstrapServers);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

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

        await ProjectionTestHelpers.WaitUntil(async () =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            return await db.BudgetSnapshotItemProjections.AnyAsync(x => x.BudgetId == budget.Id && x.BudgetItemId == salary.Id && x.Balance == -100m)
                && await db.AuditEvents.AnyAsync(x => x.BudgetId == budget.Id && x.EventType == "BudgetAdjustmentRecorded");
        });
    }

    [Fact]
    public async Task ReportingProjectionConsumerDeadLettersPoisonEventAndContinuesWithLaterEvents()
    {
        var kafkaPort = ProjectionTestHelpers.GetFreeTcpPort();
        var bootstrapServers = $"127.0.0.1:{kafkaPort}";
        await using var kafka = await ProjectionTestHelpers.StartRedpandaAsync(kafkaPort);
        await ProjectionTestHelpers.CreateKafkaTopicsAsync(bootstrapServers);

        await using var app = new KafkaBudgetApiFactory(bootstrapServers);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

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

        await ProjectionTestHelpers.WaitUntil(
            async () =>
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
                return await db.ProjectionEventFailures.AnyAsync(x => x.Status == ProjectionFailureStatus.DeadLettered)
                    && await db.AuditEventFailures.AnyAsync(x => x.Status == AuditFailureStatus.DeadLettered)
                    && await db.BudgetSnapshotItemProjections.AnyAsync(x => x.BudgetId == budget.Id && x.BudgetItemId == salary.Id && x.Balance == -100m);
            },
            async () =>
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
                var projectionFailures = await db.ProjectionEventFailures
                    .Select(x => new { x.Status, x.Category, x.LastError })
                    .ToListAsync();
                var auditFailures = await db.AuditEventFailures
                    .Select(x => new { x.Status, x.Category, x.LastError })
                    .ToListAsync();
                var snapshotItems = await db.BudgetSnapshotItemProjections
                    .Where(x => x.BudgetId == budget.Id && x.BudgetItemId == salary.Id)
                    .Select(x => new { x.Balance })
                    .ToListAsync();

                return $"Projection failures: {string.Join(", ", projectionFailures.Select(x => $"{x.Status}/{x.Category}/{x.LastError}"))}; "
                    + $"audit failures: {string.Join(", ", auditFailures.Select(x => $"{x.Status}/{x.Category}/{x.LastError}"))}; "
                    + $"snapshot balances: {string.Join(", ", snapshotItems.Select(x => x.Balance))}.";
            });
    }
}
