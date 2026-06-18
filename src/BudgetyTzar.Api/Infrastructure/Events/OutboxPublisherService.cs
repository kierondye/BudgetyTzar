using BudgetyTzar.Api.Infrastructure.Persistence;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class OutboxPublisherService(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<OutboxOptions> outboxOptions,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!outboxOptions.Value.PublisherEnabled)
        {
            logger.LogInformation("Outbox publisher is disabled.");
            return;
        }

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafkaOptions.Value.BootstrapServers,
            ClientId = kafkaOptions.Value.ClientId,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            await PublishPendingMessages(producer, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, outboxOptions.Value.PollingIntervalSeconds)), stoppingToken);
        }
    }

    private async Task PublishPendingMessages(IProducer<string, string> producer, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var batchSize = Math.Clamp(outboxOptions.Value.BatchSize, 1, 500);
        var messages = await db.OutboxMessages
            .Where(x => x.Status == OutboxMessageStatus.Pending || x.Status == OutboxMessageStatus.Failed)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (var message in messages)
        {
            try
            {
                await producer.ProduceAsync(
                    message.Topic,
                    new Message<string, string>
                    {
                        Key = message.AggregateId.ToString(),
                        Value = message.EnvelopeJson
                    },
                    ct);

                message.MarkPublished(DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to publish outbox message {OutboxMessageId}.", message.Id);
                message.MarkFailed(ex.Message);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
