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

internal sealed class BudgetApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _useProjectionBackedReports;

    public BudgetApiFactory(bool useProjectionBackedReports = false)
    {
        _useProjectionBackedReports = useProjectionBackedReports;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Outbox:PublisherEnabled", "false");
        builder.UseSetting("Projections:ConsumerEnabled", "false");
        builder.UseSetting("Projections:UseProjectionBackedReports", _useProjectionBackedReports.ToString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<BudgetDbContext>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<BudgetDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BudgetDbContext>>();
            services.RemoveAll<DbConnection>();

            services.AddSingleton<DbConnection>(_ =>
            {
                var connection = new SqliteConnection("DataSource=:memory:");
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

    public async Task<int> CountAllocationsAsync(Guid transactionId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.TransactionAllocations.CountAsync(x => x.TransactionId == transactionId);
    }

    public async Task<FinancialTransaction?> GetTransactionAsync(Guid transactionId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == transactionId);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetAuditEventsAsync(Guid budgetId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.AuditEvents
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .ToListAsync();
    }
}
