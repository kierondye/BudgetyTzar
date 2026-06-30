using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests;

public sealed class BudgetAdjustmentsTests
{
    [Fact]
    public async Task AdjustmentsSnapshotAndArchivedHistoryUsePlannedVsActualBalances()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary", BudgetItemKind.Funding);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries", BudgetItemKind.Consumption);
        var retired = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Retired", BudgetItemKind.Consumption);

        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 2_500m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 1), "Expected salary");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, groceries.Id, 500m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 2), "Expected groceries");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, retired.Id, 10m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 2), "Historical balance");
        await BudgetApiTestClient.ArchiveBudgetItem(client, budget.Id, retired.Id);

        var beforeDebit = await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budget.Id}/snapshot?date=2026-06-01");
        var afterDebit = await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budget.Id}/snapshot?date=2026-06-02");

        Assert.Equal(-2_500m, beforeDebit!.BudgetItems.Single(x => x.BudgetItemId == salary.Id).Balance);
        Assert.DoesNotContain(beforeDebit.BudgetItems, x => x.BudgetItemId == groceries.Id && x.Balance != 0);
        Assert.Equal(500m, afterDebit!.BudgetItems.Single(x => x.BudgetItemId == groceries.Id).Balance);
        Assert.Equal(10m, afterDebit.BudgetItems.Single(x => x.BudgetItemId == retired.Id).Balance);
    }

    [Fact]
    public async Task NetPlannedSpendingValidationIsScopedToTheRelevantDate()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary", BudgetItemKind.Funding);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries", BudgetItemKind.Consumption);

        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 7, 1), "Future income");
        var earlyDebit = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{groceries.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(100m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 1), "Too early"));
        var laterDebit = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{groceries.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(100m, BudgetAdjustmentType.Debit, new DateOnly(2026, 7, 2), "After income"));

        Assert.Equal(HttpStatusCode.BadRequest, earlyDebit.StatusCode);
        Assert.Equal(HttpStatusCode.Created, laterDebit.StatusCode);
    }

    [Fact]
    public async Task BudgetAdjustmentKindValidationAllowsCorrectionsButRejectsKindInversion()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary", BudgetItemKind.Funding);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries", BudgetItemKind.Consumption);

        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 1), "Expected salary");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, groceries.Id, 75m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 2), "Expected groceries");

        var groceryCorrection = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{groceries.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(25m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 3), "Reduced grocery need"));
        var groceryInversion = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{groceries.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(75.01m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 4), "Invalid grocery credit"));
        var salaryCorrection = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{salary.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(25m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 3), "Reduced salary expectation"));
        var salaryInversion = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{salary.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(75.01m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 4), "Invalid salary debit"));

        Assert.Equal(HttpStatusCode.Created, groceryCorrection.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, groceryInversion.StatusCode);
        Assert.Equal(HttpStatusCode.Created, salaryCorrection.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, salaryInversion.StatusCode);
    }

    [Fact]
    public async Task AdjustmentHandlerReturnsValidationProblemForInvalidMoneyBeforeRecordingAdjustment()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary", BudgetItemKind.Funding);

        using var scope = app.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<RecordAdjustmentHandler>();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();

        var result = await handler.Handle(
            budget.Id,
            salary.Id,
            10.001m,
            BudgetAdjustmentType.Credit,
            new DateOnly(2026, 6, 1),
            "Too precise",
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.ValidationProblem, result.Status);
        var error = Assert.Single(result.Errors![nameof(CreateBudgetItemAdjustmentRequest.Amount).ToLowerInvariant()]);
        Assert.Equal(MoneyAmount.MoneyScaleExceededMessage, error);
        Assert.Equal(0, await db.BudgetAdjustments.CountAsync());
    }
}
