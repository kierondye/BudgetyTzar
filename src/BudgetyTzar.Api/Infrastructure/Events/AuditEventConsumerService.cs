using BudgetyTzar.Api.Application.Reporting;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class AuditEventConsumerService(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<EventTopicOptions> topicOptions,
    IConfiguration configuration,
    IOptions<AuditOptions> auditOptions,
    ILogger<AuditEventConsumerService> logger) : BackgroundService
{
    public async Task<bool> ProjectEnvelope(string envelopeJson, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var schemaValidator = scope.ServiceProvider.GetRequiredService<EventSchemaValidator>();
        var projector = scope.ServiceProvider.GetRequiredService<AuditEventProjectionService>();
        var envelope = schemaValidator.ValidateAndDeserialize(envelopeJson);
        await projector.Apply(envelope, ct);
        return true;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!auditOptions.Value.ConsumerEnabled)
        {
            logger.LogInformation("Audit Kafka consumer is disabled.");
            return Task.CompletedTask;
        }

        return Task.Run(() => ConsumeEvents(stoppingToken), stoppingToken);
    }

    private async Task ConsumeEvents(CancellationToken stoppingToken)
    {
        var groupId = configuration["Kafka:ConsumerGroups:Audit"] ?? "budgetytzar-audit-local";
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafkaOptions.Value.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafkaOptions.Value.BootstrapServers,
            ClientId = $"{kafkaOptions.Value.ClientId}-audit-dlq",
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();

        consumer.Subscribe([topicOptions.Value.Budgeting, topicOptions.Value.Transactions]);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                if (await TryProjectOrDeadLetter(result, groupId, producer, stoppingToken))
                {
                    consumer.Commit(result);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to consume or project audit event.");
            }
        }
    }

    private async Task<bool> TryProjectOrDeadLetter(
        ConsumeResult<string, string> result,
        string groupId,
        IProducer<string, string> producer,
        CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, auditOptions.Value.MaxRetryAttempts);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await ProjectEnvelope(result.Message.Value, ct);
            }
            catch (PermanentProjectionException ex)
            {
                await PersistFailure(result, groupId, AuditFailureCategory.Validation, AuditFailureStatus.Pending, attempt, ex, ct);
                return await TryPublishDeadLetter(result, groupId, producer, AuditFailureCategory.Validation, attempt, ex, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
                if (attempt < maxAttempts)
                {
                    await DelayForRetry(attempt, ct);
                    continue;
                }
            }
        }

        if (lastException is null)
        {
            return false;
        }

        await PersistFailure(result, groupId, AuditFailureCategory.AuditProjection, AuditFailureStatus.Pending, maxAttempts, lastException, ct);
        return await TryPublishDeadLetter(result, groupId, producer, AuditFailureCategory.AuditProjection, maxAttempts, lastException, ct);
    }

    private async Task<bool> TryPublishDeadLetter(
        ConsumeResult<string, string> result,
        string groupId,
        IProducer<string, string> producer,
        AuditFailureCategory category,
        int retryCount,
        Exception exception,
        CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                originalTopic = result.Topic,
                partition = result.Partition.Value,
                offset = result.Offset.Value,
                consumerGroup = groupId,
                failureCategory = category.ToString(),
                retryCount,
                failureReason = exception.Message,
                failedAt = DateTimeOffset.UtcNow,
                eventJson = result.Message.Value
            }, EventSerialization.Options);

            await producer.ProduceAsync(
                auditOptions.Value.DeadLetterTopic,
                new Message<string, string>
                {
                    Key = TryReadEventId(result.Message.Value)?.ToString() ?? result.Message.Key,
                    Value = payload
                },
                ct);

            await PersistFailure(result, groupId, category, AuditFailureStatus.DeadLettered, retryCount, exception, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to publish audit dead-letter event for {TopicPartitionOffset}.", result.TopicPartitionOffset);
            await PersistFailure(result, groupId, AuditFailureCategory.DeadLetterPublish, AuditFailureStatus.Retryable, retryCount, ex, ct);
            return false;
        }
    }

    private async Task PersistFailure(
        ConsumeResult<string, string> result,
        string groupId,
        AuditFailureCategory category,
        AuditFailureStatus status,
        int retryCount,
        Exception exception,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<AuditFailureStore>();
        await store.Persist(result, groupId, category, status, retryCount, exception, ct);
    }

    private async Task DelayForRetry(int attempt, CancellationToken ct)
    {
        var initialDelay = Math.Max(1, auditOptions.Value.InitialRetryDelayMilliseconds);
        var maxDelay = Math.Max(initialDelay, auditOptions.Value.MaxRetryDelayMilliseconds);
        var delay = Math.Min(maxDelay, initialDelay * (int)Math.Pow(2, attempt - 1));
        await Task.Delay(delay, ct);
    }

    private static Guid? TryReadEventId(string eventJson) => AuditFailureStore.TryReadEventId(eventJson);
}
