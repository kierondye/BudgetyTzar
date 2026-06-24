using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Contracts.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Confluent.Kafka;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class ReportingProjectionConsumerService(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<EventTopicOptions> topicOptions,
    IConfiguration configuration,
    IOptions<ProjectionOptions> projectionOptions,
    ILogger<ReportingProjectionConsumerService> logger) : BackgroundService
{
    private static readonly string[] UpdatedReadModels = ["snapshot"];
    private readonly Guid processingInstanceId = Guid.NewGuid();

    public async Task RebuildFromOutbox(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();
        var notifications = scope.ServiceProvider.GetRequiredService<ProjectionNotificationService>();
        var schemaValidator = scope.ServiceProvider.GetRequiredService<EventSchemaValidator>();

        db.BudgetSnapshotItemProjections.RemoveRange(db.BudgetSnapshotItemProjections);
        db.BudgetSnapshotProjections.RemoveRange(db.BudgetSnapshotProjections);
        db.ProcessedProjectionEvents.RemoveRange(db.ProcessedProjectionEvents);
        db.BudgetItemProjectionStates.RemoveRange(db.BudgetItemProjectionStates);
        db.BudgetAdjustmentProjectionStates.RemoveRange(db.BudgetAdjustmentProjectionStates);
        db.TransactionAllocationProjectionStates.RemoveRange(db.TransactionAllocationProjectionStates);
        db.TransactionProjectionStates.RemoveRange(db.TransactionProjectionStates);
        await db.SaveChangesAsync(ct);

        var envelopes = await LoadOutboxEnvelopes(db, schemaValidator, null, ct);
        foreach (var envelope in envelopes)
        {
            await ProjectValidatedEnvelope(db, projector, notifications, envelope, processingInstanceId, projectionOptions.Value.ProcessingLeaseSeconds, ct);
        }
    }

    public async Task<bool> ProjectEnvelope(string envelopeJson, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var schemaValidator = scope.ServiceProvider.GetRequiredService<EventSchemaValidator>();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();
        var notifications = scope.ServiceProvider.GetRequiredService<ProjectionNotificationService>();
        var envelope = schemaValidator.ValidateAndDeserialize(envelopeJson);
        return await ProjectValidatedEnvelope(db, projector, notifications, envelope, processingInstanceId, projectionOptions.Value.ProcessingLeaseSeconds, ct);
    }

    public async Task RebuildBudget(Guid budgetId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();
        var notifications = scope.ServiceProvider.GetRequiredService<ProjectionNotificationService>();
        var schemaValidator = scope.ServiceProvider.GetRequiredService<EventSchemaValidator>();

        db.BudgetSnapshotItemProjections.RemoveRange(db.BudgetSnapshotItemProjections.Where(x => x.BudgetId == budgetId));
        db.BudgetSnapshotProjections.RemoveRange(db.BudgetSnapshotProjections.Where(x => x.BudgetId == budgetId));
        db.ProcessedProjectionEvents.RemoveRange(db.ProcessedProjectionEvents.Where(x => x.BudgetId == budgetId));
        db.BudgetItemProjectionStates.RemoveRange(db.BudgetItemProjectionStates.Where(x => x.BudgetId == budgetId));
        db.BudgetAdjustmentProjectionStates.RemoveRange(db.BudgetAdjustmentProjectionStates.Where(x => x.BudgetId == budgetId));
        db.TransactionAllocationProjectionStates.RemoveRange(db.TransactionAllocationProjectionStates.Where(x => x.BudgetId == budgetId));
        db.TransactionProjectionStates.RemoveRange(db.TransactionProjectionStates.Where(x => x.BudgetId == budgetId));
        await db.SaveChangesAsync(ct);

        var envelopes = await LoadOutboxEnvelopes(db, schemaValidator, budgetId, ct);
        foreach (var envelope in envelopes)
        {
            await ProjectValidatedEnvelope(db, projector, notifications, envelope, processingInstanceId, projectionOptions.Value.ProcessingLeaseSeconds, ct);
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!projectionOptions.Value.ConsumerEnabled)
        {
            logger.LogInformation("Reporting projection Kafka consumer is disabled.");
            return Task.CompletedTask;
        }

        return Task.Run(() => ConsumeEvents(stoppingToken), stoppingToken);
    }

    private async Task ConsumeEvents(CancellationToken stoppingToken)
    {
        var groupId = configuration["Kafka:ConsumerGroups:Reporting"] ?? "budgetytzar-reporting-local";
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
            ClientId = $"{kafkaOptions.Value.ClientId}-projection-dlq",
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
                logger.LogWarning(ex, "Failed to consume or project reporting event.");
            }
        }
    }

    private static async Task<bool> ProjectValidatedEnvelope(
        BudgetDbContext db,
        ReportingProjectionService projector,
        ProjectionNotificationService notifications,
        EventEnvelope envelope,
        Guid processingInstanceId,
        int processingLeaseSeconds,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (!await TryClaimProjectionEvent(db, envelope, processingInstanceId, processingLeaseSeconds, now, ct))
        {
            return await db.ProcessedProjectionEvents
                .AsNoTracking()
                .AnyAsync(x => x.EventId == envelope.EventId && x.Status == ProjectionProcessingStatus.Completed, ct);
        }

        ProjectionApplyResult result;
        try
        {
            result = envelope.EventType switch
            {
                "budgetytzar.budgeting.budget-created.v1" =>
                    await projector.ApplyBudgetCreated(ReadPayload<BudgetCreatedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
                "budgetytzar.budgeting.budget-item-created.v1" =>
                    await projector.ApplyBudgetItemCreated(ReadPayload<BudgetItemCreatedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
                "budgetytzar.budgeting.budget-item-archived.v1" =>
                    await projector.ApplyBudgetItemArchived(ReadPayload<BudgetItemArchivedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
                "budgetytzar.budgeting.budget-adjustment-recorded.v1" =>
                    await projector.ApplyBudgetAdjustmentRecorded(ReadPayload<BudgetAdjustmentRecordedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
                "budgetytzar.budgeting.budget-reallocation-recorded.v1" =>
                    await projector.ApplyBudgetReallocationRecorded(ReadPayload<BudgetReallocationRecordedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
                "budgetytzar.transactions.transaction-manually-created.v1" =>
                    await projector.ApplyTransactionManuallyCreated(ReadPayload<TransactionManuallyCreatedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
                "budgetytzar.transactions.transaction-edited.v1" =>
                    await projector.ApplyTransactionEdited(ReadPayload<TransactionEditedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
                "budgetytzar.transactions.transaction-ignored.v1" =>
                    await projector.ApplyTransactionIgnored(ReadPayload<TransactionIgnoredPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
                "budgetytzar.transactions.transaction-allocations-replaced.v1" =>
                    await projector.ApplyTransactionAllocationsReplaced(ReadPayload<TransactionAllocationsReplacedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
                "budgetytzar.transactions.transaction-allocations-cleared.v1" =>
                    await projector.ApplyTransactionAllocationsCleared(ReadPayload<TransactionAllocationsClearedPayload>(envelope), envelope.EventId, envelope.EventType, envelope.OccurredAt, ct),
                _ => throw new PermanentProjectionException($"No reporting projector exists for event type '{envelope.EventType}'.")
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MarkProjectionFailed(db, envelope.EventId, ex, ct);
            throw;
        }

        var projectedAt = DateTimeOffset.UtcNow;
        var projectionEvent = await db.ProcessedProjectionEvents.SingleAsync(x => x.EventId == envelope.EventId, ct);
        projectionEvent.EventType = envelope.EventType;
        projectionEvent.BudgetId = result.BudgetId;
        projectionEvent.OccurredAt = envelope.OccurredAt;
        projectionEvent.ProcessedAt = projectedAt;
        projectionEvent.Status = ProjectionProcessingStatus.Completed;
        projectionEvent.ProcessingUpdatedAt = projectedAt;
        projectionEvent.CompletedAt = projectedAt;
        projectionEvent.LastError = null;
        await db.SaveChangesAsync(ct);
        notifications.Publish(new ProjectionReadyNotification(result.BudgetId, envelope.EventId, envelope.EventType, projectedAt, UpdatedReadModels));
        return true;
    }

    private static async Task<bool> TryClaimProjectionEvent(
        BudgetDbContext db,
        EventEnvelope envelope,
        Guid processingInstanceId,
        int processingLeaseSeconds,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var leaseCutoff = now.AddSeconds(-Math.Max(1, processingLeaseSeconds));
        var projectionEvent = await db.ProcessedProjectionEvents
            .SingleOrDefaultAsync(x => x.EventId == envelope.EventId, ct);
        if (projectionEvent is not null)
        {
            if (projectionEvent.Status == ProjectionProcessingStatus.Completed)
            {
                return false;
            }

            if (projectionEvent.Status == ProjectionProcessingStatus.Processing
                && projectionEvent.ProcessingUpdatedAt >= leaseCutoff)
            {
                return false;
            }

            projectionEvent.EventType = envelope.EventType;
            projectionEvent.OccurredAt = envelope.OccurredAt;
            projectionEvent.Status = ProjectionProcessingStatus.Processing;
            projectionEvent.ProcessingInstanceId = processingInstanceId;
            projectionEvent.ProcessingStartedAt = now;
            projectionEvent.ProcessingUpdatedAt = now;
            projectionEvent.LastError = null;
            await db.SaveChangesAsync(ct);
            return true;
        }

        db.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            OccurredAt = envelope.OccurredAt,
            ProcessedAt = now,
            Status = ProjectionProcessingStatus.Processing,
            ProcessingInstanceId = processingInstanceId,
            ProcessingStartedAt = now,
            ProcessingUpdatedAt = now
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static async Task MarkProjectionFailed(BudgetDbContext db, Guid eventId, Exception exception, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var projectionEvent = await db.ProcessedProjectionEvents.SingleOrDefaultAsync(x => x.EventId == eventId, ct);
        if (projectionEvent is null)
        {
            return;
        }

        projectionEvent.Status = ProjectionProcessingStatus.Failed;
        projectionEvent.ProcessingUpdatedAt = now;
        projectionEvent.LastError = Truncate(exception.Message, 4000);
        await db.SaveChangesAsync(ct);
    }

    private static TPayload ReadPayload<TPayload>(EventEnvelope envelope)
    {
        try
        {
            return envelope.Payload.Deserialize<TPayload>(EventSerialization.Options)
                ?? throw new PermanentProjectionException($"Event payload for '{envelope.EventType}' could not be deserialized.");
        }
        catch (JsonException ex)
        {
            throw new PermanentProjectionException($"Event payload for '{envelope.EventType}' could not be deserialized.", ex);
        }
    }

    private async Task<bool> TryProjectOrDeadLetter(
        ConsumeResult<string, string> result,
        string groupId,
        IProducer<string, string> producer,
        CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, projectionOptions.Value.MaxRetryAttempts);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await ProjectEnvelope(result.Message.Value, ct);
            }
            catch (PermanentProjectionException ex)
            {
                await PersistFailure(result, groupId, ProjectionFailureCategory.Validation, ProjectionFailureStatus.Pending, attempt, ex, ct);
                return await TryPublishDeadLetter(result, groupId, producer, ProjectionFailureCategory.Validation, attempt, ex, ct);
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

        await PersistFailure(result, groupId, ProjectionFailureCategory.Projection, ProjectionFailureStatus.Pending, maxAttempts, lastException, ct);
        return await TryPublishDeadLetter(result, groupId, producer, ProjectionFailureCategory.Projection, maxAttempts, lastException, ct);
    }

    private async Task<bool> TryPublishDeadLetter(
        ConsumeResult<string, string> result,
        string groupId,
        IProducer<string, string> producer,
        ProjectionFailureCategory category,
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
                projectionOptions.Value.DeadLetterTopic,
                new Message<string, string>
                {
                    Key = TryReadEventId(result.Message.Value)?.ToString() ?? result.Message.Key,
                    Value = payload
                },
                ct);

            await PersistFailure(result, groupId, category, ProjectionFailureStatus.DeadLettered, retryCount, exception, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to publish projection dead-letter event for {TopicPartitionOffset}.", result.TopicPartitionOffset);
            await PersistFailure(result, groupId, ProjectionFailureCategory.DeadLetterPublish, ProjectionFailureStatus.Retryable, retryCount, ex, ct);
            return false;
        }
    }

    private async Task PersistFailure(
        ConsumeResult<string, string> result,
        string groupId,
        ProjectionFailureCategory category,
        ProjectionFailureStatus status,
        int retryCount,
        Exception exception,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var metadata = TryReadMetadata(result.Message.Value);
        var now = DateTimeOffset.UtcNow;
        var existing = await db.ProjectionEventFailures
            .FirstOrDefaultAsync(x => x.Topic == result.Topic && x.Partition == result.Partition.Value && x.Offset == result.Offset.Value, ct);

        if (existing is null)
        {
            db.ProjectionEventFailures.Add(new ProjectionEventFailure
            {
                EventId = metadata.EventId,
                Topic = result.Topic,
                Partition = result.Partition.Value,
                Offset = result.Offset.Value,
                ConsumerGroup = groupId,
                EventType = metadata.EventType,
                BudgetId = metadata.BudgetId,
                Category = category,
                Status = status,
                RetryCount = retryCount,
                LastError = Truncate(exception.Message, 4000),
                RawEventJson = result.Message.Value,
                FirstFailedAt = now,
                LastFailedAt = now
            });
        }
        else
        {
            existing.EventId = metadata.EventId ?? existing.EventId;
            existing.EventType = metadata.EventType ?? existing.EventType;
            existing.BudgetId = metadata.BudgetId ?? existing.BudgetId;
            existing.Category = category;
            existing.Status = status;
            existing.RetryCount = retryCount;
            existing.LastError = Truncate(exception.Message, 4000);
            existing.RawEventJson = result.Message.Value;
            existing.LastFailedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task DelayForRetry(int attempt, CancellationToken ct)
    {
        var initialDelay = Math.Max(1, projectionOptions.Value.InitialRetryDelayMilliseconds);
        var maxDelay = Math.Max(initialDelay, projectionOptions.Value.MaxRetryDelayMilliseconds);
        var delay = Math.Min(maxDelay, initialDelay * (int)Math.Pow(2, attempt - 1));
        await Task.Delay(delay, ct);
    }

    private static async Task<IReadOnlyList<EventEnvelope>> LoadOutboxEnvelopes(
        BudgetDbContext db,
        EventSchemaValidator schemaValidator,
        Guid? budgetId,
        CancellationToken ct)
    {
        var query = db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.BudgetId != null);
        if (budgetId.HasValue)
        {
            query = query.Where(x => x.BudgetId == budgetId.Value);
        }

        var messages = await query
            .Select(x => new { x.CreatedAt, x.EnvelopeJson })
            .ToListAsync(ct);

        return messages
            .OrderBy(x => x.CreatedAt)
            .Select(x => schemaValidator.ValidateAndDeserialize(x.EnvelopeJson))
            .ToList();
    }

    private static (Guid? EventId, string? EventType, Guid? BudgetId) TryReadMetadata(string eventJson)
    {
        try
        {
            using var document = JsonDocument.Parse(eventJson);
            var root = document.RootElement;
            var eventId = root.TryGetProperty("eventId", out var eventIdElement) && eventIdElement.TryGetGuid(out var parsedEventId)
                ? parsedEventId
                : (Guid?)null;
            var eventType = root.TryGetProperty("eventType", out var eventTypeElement)
                ? eventTypeElement.GetString()
                : null;
            Guid? budgetId = null;
            if (root.TryGetProperty("payload", out var payload)
                && payload.TryGetProperty("budgetId", out var budgetIdElement)
                && budgetIdElement.TryGetGuid(out var parsedBudgetId))
            {
                budgetId = parsedBudgetId;
            }

            return (eventId, eventType, budgetId);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static Guid? TryReadEventId(string eventJson) => TryReadMetadata(eventJson).EventId;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
