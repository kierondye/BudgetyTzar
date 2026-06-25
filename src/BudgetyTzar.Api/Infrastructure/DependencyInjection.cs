using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBudgetyTzarInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BudgetDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("BudgetyTzar")));

        services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
        services.Configure<KafkaTopicOptions>(configuration.GetSection("Kafka:TopicManagement"));
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));
        services.Configure<ProjectionOptions>(configuration.GetSection("Projections"));
        services.Configure<AuditOptions>(configuration.GetSection("Audit"));
        services.Configure<EventTopicOptions>(options =>
        {
            options.Budgeting = configuration["Kafka:Topics:BudgetingEvents"] ?? options.Budgeting;
            options.Transactions = configuration["Kafka:Topics:TransactionEvents"] ?? options.Transactions;
            options.Reporting = configuration["Kafka:Topics:ReportingEvents"] ?? options.Reporting;
        });

        services.AddScoped<DomainEventOutboxWriter>();
        services.AddScoped<ProjectionProcessingStore>();
        services.AddScoped<ProjectionFailureStore>();
        services.AddSingleton<EventSchemaValidator>();
        services.AddSingleton<ReportingProjectionConsumerService>();
        services.AddSingleton<AuditEventConsumerService>();
        services.AddSingleton<ProjectionNotificationService>();
        services.AddHostedService<KafkaTopicInitializerService>();
        services.AddHostedService<OutboxPublisherService>();
        services.AddHostedService(sp => sp.GetRequiredService<ReportingProjectionConsumerService>());
        services.AddHostedService(sp => sp.GetRequiredService<AuditEventConsumerService>());
        return services;
    }
}
