using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Confluent.Kafka;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests;

public sealed class KafkaProjectionConsumerTests
{
    [Fact]
    public async Task ReportingProjectionConsumerProjectsEventsConsumedFromKafka()
    {
        var kafkaPort = ProjectionTestHelpers.GetFreeTcpPort();
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
            return await db.BudgetSnapshotItemProjections.AnyAsync(x => x.BudgetId == budget.Id && x.BudgetItemId == salary.Id && x.Balance == -100m);
        });
    }

    [Fact]
    public async Task ReportingProjectionConsumerDeadLettersPoisonEventAndContinuesWithLaterEvents()
    {
        var kafkaPort = ProjectionTestHelpers.GetFreeTcpPort();
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

        await ProjectionTestHelpers.WaitUntil(async () =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            return await db.ProjectionEventFailures.AnyAsync(x => x.Status == ProjectionFailureStatus.DeadLettered)
                && await db.BudgetSnapshotItemProjections.AnyAsync(x => x.BudgetId == budget.Id && x.BudgetItemId == salary.Id && x.Balance == -100m);
        });
    }
}
