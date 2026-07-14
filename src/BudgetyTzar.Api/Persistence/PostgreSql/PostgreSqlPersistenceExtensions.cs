using BudgetyTzar.Api.Features.Audit;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public static class PostgreSqlPersistenceExtensions
{
    public static IServiceCollection AddPostgreSqlPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });
        services.AddHealthChecks()
            .AddCheck<PostgreSqlDatabaseHealthCheck>("postgresql", tags: ["ready"]);
        services.AddScoped<IApplicationUserStore, PostgreSqlApplicationUserStore>();
        services.AddScoped<IAuditRecorder, PostgreSqlAuditRecorder>();
        services.AddScoped<IAuditOperationRunner, PostgreSqlAuditOperationRunner>();
        services.AddScoped<IBudgetRepository, PostgreSqlBudgetRepository>();
        services.AddScoped<ITransactionRepository, PostgreSqlTransactionRepository>();
        services.AddScoped<ITransactionAllocationRepository, PostgreSqlTransactionAllocationRepository>();

        return services;
    }
}
