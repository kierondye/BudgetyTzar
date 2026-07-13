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

        return services;
    }
}
