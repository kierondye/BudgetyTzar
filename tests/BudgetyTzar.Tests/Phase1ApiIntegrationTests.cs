using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Transactions;
using BudgetyTzar.Api.Infrastructure.Persistence;
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
            $"/api/budgets/{budget.Id}/transactions/{refund.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAssignmentItem(groceries.Id, 25m)]));

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
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([]));

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
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAssignmentItem(groceries.Id, 20.01m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TransactionAllocationAliasCanClearToEmpty()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, 20m, TransactionDirection.Debit);
        var assignResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAssignmentItem(groceries.Id, 20m)]));
        assignResponse.EnsureSuccessStatusCode();

        var clearResponse = await client.DeleteAsync($"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations");
        var allocations = await client.GetFromJsonAsync<IReadOnlyList<TransactionAssignment>>(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations");

        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);
        Assert.Empty(allocations!);
        Assert.Equal(0, await app.CountAssignmentsAsync(transaction.Id));
    }

    [Fact]
    public async Task TransactionAllocationAliasCanReplaceWithSplitAllocations()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var household = await CreateBudgetLine(client, budget.Id, "Household", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, 50m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([
                new TransactionAssignmentItem(groceries.Id, 30m),
                new TransactionAssignmentItem(household.Id, 15m)
            ]));
        var allocations = await client.GetFromJsonAsync<IReadOnlyList<TransactionAssignment>>(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2, allocations!.Count);
        Assert.Contains(allocations, x => x.BudgetLineId == groceries.Id && x.Amount == 30m);
        Assert.Contains(allocations, x => x.BudgetLineId == household.Id && x.Amount == 15m);
    }

    [Fact]
    public async Task TransactionAllocationAliasRejectsOverAllocation()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, 20m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAssignmentItem(groceries.Id, 20.01m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await app.CountAssignmentsAsync(transaction.Id));
    }

    [Fact]
    public async Task TransactionAllocationAliasCanGetExistingAssignments()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, 20m, TransactionDirection.Debit);
        var assignResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAssignmentItem(groceries.Id, 12m)]));
        assignResponse.EnsureSuccessStatusCode();

        var allocations = await client.GetFromJsonAsync<IReadOnlyList<TransactionAssignment>>(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations");

        var allocation = Assert.Single(allocations!);
        Assert.Equal(groceries.Id, allocation.BudgetLineId);
        Assert.Equal(12m, allocation.Amount);
    }

    [Fact]
    public async Task TransactionEditWritesAuditAndPreservesAssignments()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, 25m, TransactionDirection.Debit);
        var assignResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAssignmentItem(groceries.Id, 20m)]));
        assignResponse.EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}",
            new UpdateTransactionRequest(
                new DateOnly(2026, 6, 11),
                "Edited groceries",
                30m,
                TransactionDirection.Debit,
                "Current account",
                "EDIT-1",
                "Updated note"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var edited = await app.GetTransactionAsync(transaction.Id);
        Assert.Equal(transaction.Id, edited!.Id);
        Assert.Equal("Edited groceries", edited.Description);
        Assert.Equal(30m, edited.Amount);
        Assert.Equal(1, await app.CountAssignmentsAsync(transaction.Id));

        var audit = await app.GetAuditEventsAsync(budget.Id);
        var editAudit = audit.Single(x => x.EventType == "TransactionEdited");
        Assert.NotEqual(Guid.Empty, editAudit.Id);
        Assert.Equal(nameof(FinancialTransaction), editAudit.EntityType);
        Assert.Equal(transaction.Id, editAudit.EntityId);
    }

    [Fact]
    public async Task TransactionAmountCannotBeEditedBelowCurrentAssignmentTotal()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, 25m, TransactionDirection.Debit);
        var assignResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAssignmentItem(groceries.Id, 20m)]));
        assignResponse.EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}",
            new UpdateTransactionRequest(
                transaction.TransactionDate,
                transaction.Description,
                19.99m,
                transaction.Direction,
                transaction.SourceAccount,
                transaction.ExternalReference,
                transaction.Notes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, await app.CountAssignmentsAsync(transaction.Id));
        var persisted = await app.GetTransactionAsync(transaction.Id);
        Assert.Equal(25m, persisted!.Amount);
    }

    [Fact]
    public async Task TransactionImportPreviewCommitAndRecommitAreIdempotent()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions",
            new CreateTransactionRequest(
                new DateOnly(2026, 6, 9),
                "Existing shop",
                12.34m,
                TransactionDirection.Debit,
                "Current account",
                "EXT-1",
                null));
        var csv = """
date,description,amount,direction,source account,external reference,notes
2026-06-09,Existing shop,12.34,Debit,Current account,EXT-1,
2026-06-10,Salary,2500.00,Credit,Current account,EXT-2,June pay
""";

        var previewResponse = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/transaction-imports/preview",
            new PreviewTransactionImportRequest("transactions.csv", csv));
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<TransactionImportDetail>())!;

        Assert.Equal(2, preview.Rows.Count);
        Assert.Contains(preview.Rows, x => x.IsDuplicateCandidate);

        var commitResponse = await client.PostAsync($"/api/budgets/{budget.Id}/transaction-imports/{preview.Batch.Id}/commit", null);
        var recommitResponse = await client.PostAsync($"/api/budgets/{budget.Id}/transaction-imports/{preview.Batch.Id}/commit", null);

        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recommitResponse.StatusCode);
        Assert.Equal(3, await app.CountTransactionsAsync(budget.Id));
    }

    private static async Task<Budget> CreateBudget(HttpClient client, string name = "Personal")
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest(name, "GBP"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Budget>())!;
    }

    private static async Task<BudgetItemDto> CreateBudgetLine(
        HttpClient client,
        Guid budgetId,
        string name,
        BudgetLineDirection direction)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items",
            new CreateBudgetItemRequest(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetItemDto>())!;
    }

    private static async Task ArchiveBudgetLine(HttpClient client, Guid budgetId, Guid lineId)
    {
        var response = await client.PostAsync($"/api/budgets/{budgetId}/budget-items/{lineId}/archive", null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task RecordBudgetItemAdjustment(
        HttpClient client,
        Guid budgetId,
        Guid budgetItemId,
        decimal amount,
        BudgetAdjustmentType type,
        DateOnly date)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments",
            new CreateBudgetItemAdjustmentRequest(amount, type, date, null));
        response.EnsureSuccessStatusCode();
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
    private readonly bool _useProjectionBackedReports;

    public BudgetApiFactory(bool useProjectionBackedReports = false)
    {
        _useProjectionBackedReports = useProjectionBackedReports;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:MigrateOnStartup", "false");
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

    public async Task<int> CountAssignmentsAsync(Guid transactionId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.TransactionAssignments.CountAsync(x => x.TransactionId == transactionId);
    }

    public async Task<int> CountTransactionsAsync(Guid budgetId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.Transactions.CountAsync(x => x.BudgetId == budgetId);
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
