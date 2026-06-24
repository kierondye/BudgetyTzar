using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class KafkaTopicInitializerService(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<EventTopicOptions> topicOptions,
    IOptions<KafkaTopicOptions> kafkaTopicOptions,
    IOptions<OutboxOptions> outboxOptions,
    IOptions<ProjectionOptions> projectionOptions,
    IOptions<AuditOptions> auditOptions,
    ILogger<KafkaTopicInitializerService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!kafkaTopicOptions.Value.AutoCreateTopics
            || (!outboxOptions.Value.PublisherEnabled
                && !projectionOptions.Value.ConsumerEnabled
                && !auditOptions.Value.ConsumerEnabled))
        {
            logger.LogInformation("Kafka topic initialization is disabled.");
            return;
        }

        var topics = new[]
            {
                topicOptions.Value.Budgeting,
                topicOptions.Value.Transactions,
                topicOptions.Value.Reporting,
                projectionOptions.Value.DeadLetterTopic,
                auditOptions.Value.DeadLetterTopic
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Select(x => new TopicSpecification
            {
                Name = x,
                NumPartitions = Math.Max(1, kafkaTopicOptions.Value.Partitions),
                ReplicationFactor = Math.Max((short)1, kafkaTopicOptions.Value.ReplicationFactor)
            })
            .ToList();

        if (topics.Count == 0)
        {
            return;
        }

        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = kafkaOptions.Value.BootstrapServers,
            ClientId = $"{kafkaOptions.Value.ClientId}-topic-initializer"
        }).Build();

        try
        {
            await admin.CreateTopicsAsync(topics, new CreateTopicsOptions
            {
                OperationTimeout = TimeSpan.FromSeconds(Math.Max(1, kafkaTopicOptions.Value.OperationTimeoutSeconds)),
                RequestTimeout = TimeSpan.FromSeconds(Math.Max(1, kafkaTopicOptions.Value.RequestTimeoutSeconds))
            });
            logger.LogInformation("Created Kafka topics: {Topics}.", string.Join(", ", topics.Select(x => x.Name)));
        }
        catch (CreateTopicsException ex) when (ex.Results.All(x => x.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            logger.LogInformation("Kafka topics already exist: {Topics}.", string.Join(", ", topics.Select(x => x.Name)));
        }
        catch (CreateTopicsException ex)
        {
            var nonExistingTopicErrors = ex.Results
                .Where(x => x.Error.Code != ErrorCode.TopicAlreadyExists)
                .Select(x => $"{x.Topic}: {x.Error.Reason}")
                .ToList();
            if (nonExistingTopicErrors.Count > 0)
            {
                logger.LogWarning("Kafka topic initialization failed for {TopicErrors}.", string.Join("; ", nonExistingTopicErrors));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Kafka topic initialization failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
