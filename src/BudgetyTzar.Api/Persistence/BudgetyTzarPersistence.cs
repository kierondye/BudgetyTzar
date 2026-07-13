using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Transactions;
using BudgetyTzar.Api.Persistence.PostgreSql;

namespace BudgetyTzar.Api.Persistence;

public static class BudgetyTzarPersistence
{
    public static IServiceCollection AddBudgetyTzarPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = PersistenceOptions.FromConfiguration(configuration);

        if (options.Provider == PersistenceProvider.InMemory)
        {
            services.AddBudgeting();
            services.AddTransactions();
            return services;
        }

        services.AddBudgetyTzarPostgreSqlPersistence(options.PostgreSqlConnectionString!);
        return services;
    }
}

public sealed record PersistenceOptions
{
    public PersistenceProvider Provider { get; init; } = PersistenceProvider.InMemory;

    public string? PostgreSqlConnectionString { get; init; }

    public static PersistenceOptions FromConfiguration(IConfiguration configuration)
    {
        var providerValue = configuration["Persistence:Provider"];
        var provider = ParseProvider(providerValue);
        var connectionString = NullIfWhiteSpace(configuration.GetConnectionString("BudgetyTzar"))
            ?? NullIfWhiteSpace(configuration["Persistence:ConnectionString"])
            ?? NullIfWhiteSpace(configuration["Persistence:PostgreSql:ConnectionString"]);

        if (provider == PersistenceProvider.PostgreSql && string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Persistence:Provider=PostgreSql requires ConnectionStrings:BudgetyTzar, Persistence:ConnectionString, or Persistence:PostgreSql:ConnectionString.");
        }

        return new PersistenceOptions
        {
            Provider = provider,
            PostgreSqlConnectionString = connectionString
        };
    }

    private static PersistenceProvider ParseProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PersistenceProvider.InMemory;
        }

        if (string.Equals(value, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return PersistenceProvider.InMemory;
        }

        if (string.Equals(value, "PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return PersistenceProvider.PostgreSql;
        }

        throw new InvalidOperationException(
            "Persistence:Provider must be InMemory or PostgreSql.");
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public enum PersistenceProvider
{
    InMemory,
    PostgreSql
}
