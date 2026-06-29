using BudgetyTzar.Api;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BudgetyTzar.Tests;

internal sealed class KafkaBudgetApiFactory(string bootstrapServers) : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"budgetytzar-kafka-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Kafka:BootstrapServers", bootstrapServers);
        builder.UseSetting("Kafka:TopicManagement:AutoCreateTopics", "false");
        builder.UseSetting("Outbox:PublisherEnabled", "false");
        builder.UseSetting("Audit:ConsumerEnabled", "true");
        builder.UseSetting("Audit:MaxRetryAttempts", "1");
        builder.UseSetting("Audit:InitialRetryDelayMilliseconds", "10");
        builder.UseSetting("Audit:MaxRetryDelayMilliseconds", "10");
        builder.UseSetting("Projections:ConsumerEnabled", "true");
        builder.UseSetting("Projections:UseProjectionBackedReports", "true");
        builder.UseSetting("Projections:MaxRetryAttempts", "1");
        builder.UseSetting("Projections:InitialRetryDelayMilliseconds", "10");
        builder.UseSetting("Projections:MaxRetryDelayMilliseconds", "10");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<BudgetDbContext>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<BudgetDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BudgetDbContext>>();

            services.AddDbContext<BudgetDbContext>((provider, options) =>
                options.UseSqlite($"Data Source={_databasePath};Default Timeout=30"));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
