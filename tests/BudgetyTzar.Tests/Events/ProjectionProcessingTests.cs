using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests;

public sealed class ProjectionProcessingTests
{
    [Fact]
    public async Task ProjectionEnvelopeProcessingIsIdempotentForDuplicateEvents()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var envelopeJson = (await db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.EventType == "budgetytzar.budgeting.budget-adjustment-recorded.v1")
            .SingleAsync())
            .EnvelopeJson;
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();

        await projector.ProjectEnvelope(envelopeJson, CancellationToken.None);
        await projector.ProjectEnvelope(envelopeJson, CancellationToken.None);

        Assert.Equal(1, await db.ProcessedProjectionEvents.CountAsync());
        Assert.Equal(1, await db.BudgetSnapshotProjections.CountAsync(x => x.BudgetId == budget.Id && x.Date == new DateOnly(2026, 6, 10)));
    }

    [Fact]
    public async Task ProjectionEnvelopeProcessingMarksOnlyTheConsumedOutboxMessageProjected()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var consumedMessage = await db.OutboxMessages
            .AsNoTracking()
            .SingleAsync(x => x.EventType == "budgetytzar.budgeting.budget-adjustment-recorded.v1");
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();

        await projector.ProjectEnvelope(consumedMessage.EnvelopeJson, CancellationToken.None);

        var outbox = await db.OutboxMessages.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync();
        Assert.NotNull(outbox.Single(x => x.Id == consumedMessage.Id).ProjectedAt);
        Assert.All(outbox.Where(x => x.Id != consumedMessage.Id), x => Assert.Null(x.ProjectedAt));
    }

    [Fact]
    public async Task DeltaProjectionProcessingUpdatesProjectionStateWithoutImplicitOutboxReplay()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var messages = (await db.OutboxMessages.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync())
            .OrderBy(x => x.CreatedAt)
            .ToList();
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();

        foreach (var message in messages)
        {
            await projector.ProjectEnvelope(message.EnvelopeJson, CancellationToken.None);
        }

        Assert.Equal(messages.Count, await db.ProcessedProjectionEvents.CountAsync(x => x.BudgetId == budget.Id));
        Assert.Single(await db.BudgetItemProjectionStates.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync());
        Assert.Single(await db.BudgetAdjustmentProjectionStates.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync());
        Assert.True(await db.BudgetSnapshotItemProjections.AnyAsync(x => x.BudgetId == budget.Id && x.BudgetItemId == salary.Id && x.Balance == -100m));
    }
}
