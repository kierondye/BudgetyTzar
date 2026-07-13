using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class BudgetyTzarDbContextFactory : IDesignTimeDbContextFactory<BudgetyTzarDbContext>
{
    public BudgetyTzarDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("BUDGETYTZAR_MIGRATIONS_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set BUDGETYTZAR_MIGRATIONS_CONNECTION_STRING before running EF Core migration commands.");
        }

        var options = new DbContextOptionsBuilder<BudgetyTzarDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BudgetyTzarDbContext(options);
    }
}
