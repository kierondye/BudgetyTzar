using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public static class PostgreSqlPersistenceExtensions
{
    public static IServiceCollection AddBudgetyTzarPostgreSqlPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<BudgetyTzarDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });
        services.AddScoped<IApplicationUserStore, PostgreSqlApplicationUserStore>();
        services.AddScoped<IBudgetRepository, PostgreSqlBudgetRepository>();
        services.AddScoped<ITransactionRepository, PostgreSqlTransactionRepository>();
        services.AddScoped<ITransactionAllocationRepository, PostgreSqlTransactionAllocationRepository>();

        return services;
    }
}
