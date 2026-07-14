using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class PostgreSqlDatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var connection = dbContext.Database.GetDbConnection();

            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";

            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (result is 1)
            {
                return HealthCheckResult.Healthy();
            }

            return HealthCheckResult.Unhealthy("PostgreSQL health query returned an unexpected result.");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("PostgreSQL database is unreachable.");
        }
    }
}
