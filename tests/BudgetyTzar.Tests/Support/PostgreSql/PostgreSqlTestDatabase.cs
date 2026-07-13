using Testcontainers.PostgreSql;

namespace BudgetyTzar.Tests.Support.PostgreSql;

public sealed class PostgreSqlTestDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync()
    {
        return container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await container.DisposeAsync();
    }
}
