namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:19092";
    public string ClientId { get; set; } = "budgetytzar-api-local";
}

public sealed class OutboxOptions
{
    public bool PublisherEnabled { get; set; }
    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
}

public sealed class ProjectionOptions
{
    public bool ConsumerEnabled { get; set; }
    public bool UseProjectionBackedReports { get; set; }
}
