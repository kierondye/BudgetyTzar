using BudgetyTzar.Api;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace BudgetyTzar.Tests;

internal sealed class PostgresBudgetApiFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("budgetytzar_tests")
        .WithUsername("budgetytzar")
        .WithPassword("budgetytzar")
        .Build();

    public static async Task<PostgresBudgetApiFactory> StartAsync()
    {
        var factory = new PostgresBudgetApiFactory();
        await factory._postgres.StartAsync();
        return factory;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Outbox:PublisherEnabled", "false");
        builder.UseSetting("Audit:ConsumerEnabled", "false");
        builder.UseSetting("Projections:ConsumerEnabled", "false");
        builder.UseSetting("Projections:UseProjectionBackedReports", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<BudgetDbContext>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<BudgetDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BudgetDbContext>>();

            services.AddDbContext<BudgetDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task<FinancialTransaction?> GetTransactionAsync(Guid transactionId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == transactionId);
    }

    public async Task<TransactionAllocation?> GetAllocationAsync(Guid transactionId, Guid budgetItemId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.TransactionAllocations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId && x.BudgetItemId == budgetItemId);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
