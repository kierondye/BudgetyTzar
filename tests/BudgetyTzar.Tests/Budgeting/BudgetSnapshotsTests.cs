using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests;

public sealed class BudgetSnapshotsTests
{
    [Fact]
    public async Task BudgetSnapshotTracksPlannedActualAndUnbudgetedBalances()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary");
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var mortgage = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Mortgage");
        var incidentals = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Incidentals");

        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 2_500m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 18), "Initial budget for salary.");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, groceries.Id, 500m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 18), "Initial budget for groceries.");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, mortgage.Id, 800m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 18), "Initial budget for mortgage.");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, incidentals.Id, 1_000m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 18), "Initial budget for incidentals.");

        var initialSnapshot = await BudgetApiTestClient.GetSnapshot(client, budget.Id, new DateOnly(2026, 6, 18));
        BudgetApiTestClient.AssertSnapshot(
            initialSnapshot,
            -200m,
            0m,
            0m,
            -200m,
            [
                (salary.Id, -2_500m, 2_500m, 0m, 0m, 0m),
                (groceries.Id, 500m, 0m, 500m, 0m, 0m),
                (mortgage.Id, 800m, 0m, 800m, 0m, 0m),
                (incidentals.Id, 1_000m, 0m, 1_000m, 0m, 0m)
            ]);

        var pay = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 19), 3_000m, TransactionDirection.Credit);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, pay.Id, [new TransactionAllocationItem(salary.Id, 3_000m)]);

        var afterSalarySnapshot = await BudgetApiTestClient.GetSnapshot(client, budget.Id, new DateOnly(2026, 6, 19));
        BudgetApiTestClient.AssertSnapshot(
            afterSalarySnapshot,
            3_000m,
            200m,
            3_000m,
            2_800m,
            [
                (salary.Id, 500m, 2_500m, 0m, 3_000m, 0m),
                (groceries.Id, 500m, 0m, 500m, 0m, 0m),
                (mortgage.Id, 800m, 0m, 800m, 0m, 0m),
                (incidentals.Id, 1_000m, 0m, 1_000m, 0m, 0m)
            ]);

        var supermarket = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 20), 200m, TransactionDirection.Debit);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, supermarket.Id, [
            new TransactionAllocationItem(groceries.Id, 150m),
            new TransactionAllocationItem(incidentals.Id, 40m)
        ]);

        var afterSpendSnapshot = await BudgetApiTestClient.GetSnapshot(client, budget.Id, new DateOnly(2026, 6, 21));
        BudgetApiTestClient.AssertSnapshot(
            afterSpendSnapshot,
            2_800m,
            190m,
            2_800m,
            2_610m,
            [
                (salary.Id, 500m, 2_500m, 0m, 3_000m, 0m),
                (groceries.Id, 350m, 0m, 500m, 0m, 150m),
                (mortgage.Id, 800m, 0m, 800m, 0m, 0m),
                (incidentals.Id, 960m, 0m, 1_000m, 0m, 40m)
            ]);

        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 2_500m, BudgetAdjustmentType.Credit, new DateOnly(2026, 7, 18), "Second expected salary.");

        var julySnapshot = await BudgetApiTestClient.GetSnapshot(client, budget.Id, new DateOnly(2026, 7, 18));
        BudgetApiTestClient.AssertSnapshot(
            julySnapshot,
            300m,
            190m,
            2_800m,
            110m,
            [
                (salary.Id, -2_000m, 5_000m, 0m, 3_000m, 0m),
                (groceries.Id, 350m, 0m, 500m, 0m, 150m),
                (mortgage.Id, 800m, 0m, 800m, 0m, 0m),
                (incidentals.Id, 960m, 0m, 1_000m, 0m, 40m)
            ]);
    }

    [Fact]
    public async Task ProjectionBackedSnapshotReturnsZeroBalancesForBudgetItemsWithoutActivity()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");

        using (var scope = app.Services.CreateScope())
        {
            var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionConsumerService>();
            await projector.RebuildFromOutbox(CancellationToken.None);
        }

        var snapshot = await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budget.Id}/snapshot?date=2026-06-30");

        var item = Assert.Single(snapshot!.BudgetItems);
        Assert.Equal(groceries.Id, item.BudgetItemId);
        Assert.Equal(0m, item.Balance);
    }

    [Fact]
    public async Task ProjectionRebuildFromOutboxSupportsProjectionBackedSnapshots()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary");
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, groceries.Id, 100m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 10));
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 12), 35m, TransactionDirection.Debit);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, transaction.Id, [new TransactionAllocationItem(groceries.Id, 35m)]);

        using (var scope = app.Services.CreateScope())
        {
            var purgeDb = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            await purgeDb.TransactionAllocations.ExecuteDeleteAsync();
            await purgeDb.Transactions.ExecuteDeleteAsync();
            await purgeDb.BudgetAdjustments.ExecuteDeleteAsync();
            await purgeDb.BudgetItems.ExecuteDeleteAsync();

            var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionConsumerService>();
            await projector.RebuildFromOutbox(CancellationToken.None);
        }

        using var verifyScope = app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var snapshot = await db.BudgetSnapshotProjections.AsNoTracking().SingleAsync(x => x.BudgetId == budget.Id && x.Date == new DateOnly(2026, 6, 12));
        var item = await db.BudgetSnapshotItemProjections.AsNoTracking().SingleAsync(x => x.SnapshotId == snapshot.Id && x.BudgetItemId == groceries.Id);

        Assert.Equal(65m, item.Balance);
        Assert.Equal(-35m, snapshot.TotalBalance);

        var apiSnapshot = await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budget.Id}/snapshot?date=2026-06-30");
        Assert.Equal(65m, apiSnapshot!.BudgetItems.Single(x => x.BudgetItemId == groceries.Id).Balance);

        var apiAuditEvents = await client.GetFromJsonAsync<IReadOnlyList<AuditEventDto>>(
            $"/api/budgets/{budget.Id}/audit-events");
        Assert.Contains(apiAuditEvents!, x => x.EventType == "TransactionAllocationsReplaced");
    }
}
