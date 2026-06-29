using System.Data.Common;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BudgetyTzar.Tests;

internal sealed class KafkaBudgetApiFactory(string bootstrapServers) : WebApplicationFactory<Program>
{
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
            services.RemoveAll<DbConnection>();

            services.AddSingleton<DbConnection>(_ =>
            {
                var connection = new SqliteConnection("DataSource=:memory:;Default Timeout=30");
                connection.Open();
                return connection;
            });

            services.AddDbContext<BudgetDbContext>((provider, options) =>
                options.UseSqlite(provider.GetRequiredService<DbConnection>()));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }
}
