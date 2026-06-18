using BudgetyTzar.Api.Application.Reporting;
using Confluent.Kafka;
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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!projectionOptions.Value.ConsumerEnabled)
        {
            logger.LogInformation("Reporting projection Kafka consumer is disabled.");
            return;
        }

        var groupId = configuration["Kafka:ConsumerGroups:Reporting"] ?? "budgetytzar-reporting-local";
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafkaOptions.Value.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();

        consumer.Subscribe([topicOptions.Value.Budgeting, topicOptions.Value.Transactions]);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                using var scope = scopeFactory.CreateScope();
                var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();
                await projector.ProjectEnvelope(result.Message.Value, stoppingToken);
                consumer.Commit(result);
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
}
