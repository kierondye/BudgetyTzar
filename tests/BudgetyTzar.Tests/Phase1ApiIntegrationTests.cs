using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Data;
using BudgetyTzar.Api.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BudgetyTzar.Tests;

public sealed class Phase1ApiIntegrationTests
{
    [Fact]
    public async Task OppositeDirectionTransactionAssignmentIsAccepted()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var refund = await CreateTransaction(client, budget.Id, 25m, TransactionDirection.Credit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{refund.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(groceries.Id, 25m)]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task EmptyTransactionAssignmentReplacementIsAccepted()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var transaction = await CreateTransaction(client, budget.Id, 15m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await app.CountAssignmentsAsync(transaction.Id));
    }

    [Fact]
    public async Task TransactionAssignmentsCannotExceedTransactionAmount()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, 20m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(groceries.Id, 20.01m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReallocationsCannotUseCreditBudgetLines()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var salary = await CreateBudgetLine(client, budget.Id, "Salary", BudgetLineDirection.Credit);

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods/{period.Id}/reallocations",
            new CreateBudgetReallocationRequest(groceries.Id, salary.Id, 10m, "Invalid credit line target"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BudgetPeriodsCannotOverlapWithinBudget()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        await CreatePeriod(client, budget.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods",
            new CreateBudgetPeriodRequest("Overlap", new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 14)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<Budget> CreateBudget(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Budget>())!;
    }

    private static async Task<BudgetPeriod> CreatePeriod(HttpClient client, Guid budgetId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/periods",
            new CreateBudgetPeriodRequest("June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetPeriod>())!;
    }

    private static async Task<BudgetLine> CreateBudgetLine(
        HttpClient client,
        Guid budgetId,
        string name,
        BudgetLineDirection direction)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-lines",
            new CreateBudgetLineRequest(name, direction, BudgetLineRolloverType.PeriodReset));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetLine>())!;
    }

    private static async Task<FinancialTransaction> CreateTransaction(
        HttpClient client,
        Guid budgetId,
        decimal amount,
        TransactionDirection direction)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions",
            new CreateTransactionRequest(
                new DateOnly(2026, 6, 10),
                $"{direction} transaction",
                amount,
                direction,
                "Current account",
                null,
                null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FinancialTransaction>())!;
    }
}

internal sealed class BudgetApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:EnsureCreatedOnStartup", "false");
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

    public async Task<int> CountAssignmentsAsync(Guid transactionId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.TransactionAssignments.CountAsync(x => x.TransactionId == transactionId);
    }
}
