namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:19092";
    public string ClientId { get; set; } = "budgetytzar-api-local";
}

public sealed class KafkaTopicOptions
{
    public bool AutoCreateTopics { get; set; } = true;
    public int Partitions { get; set; } = 1;
    public short ReplicationFactor { get; set; } = 1;
    public int OperationTimeoutSeconds { get; set; } = 10;
    public int RequestTimeoutSeconds { get; set; } = 10;
}

public sealed class OutboxOptions
{
    public bool PublisherEnabled { get; set; }
    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
    public int PublishingLeaseSeconds { get; set; } = 60;
}

public sealed class ProjectionOptions
{
    public bool ConsumerEnabled { get; set; }
    public bool UseProjectionBackedReports { get; set; }
    public int MaxRetryAttempts { get; set; } = 3;
    public int InitialRetryDelayMilliseconds { get; set; } = 1000;
    public int MaxRetryDelayMilliseconds { get; set; } = 30000;
    public string DeadLetterTopic { get; set; } = "budgetytzar.reporting.dead-letter-events";
}
