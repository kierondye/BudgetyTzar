using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Contracts.Events;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests;

public sealed class ProjectionProcessingTests
{
    [Fact]
    public async Task ReportingProjectionServiceAppliesTypedPayloadsWithoutEventEnvelope()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        await app.ResetDatabaseAsync();

        var budgetId = Guid.NewGuid();
        var budgetItemId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();

        var result = await projector.ApplyBudgetItemCreated(
            new BudgetItemCreatedPayload(budgetId, budgetItemId, "Salary", BudgetItemKind.Funding),
            eventId,
            "budgetytzar.budgeting.budget-item-created.v1",
            occurredAt,
            CancellationToken.None);

        Assert.Equal(budgetId, result.BudgetId);
        Assert.True(await db.BudgetItemProjectionStates.AnyAsync(x =>
            x.BudgetId == budgetId
            && x.BudgetItemId == budgetItemId
            && x.Name == "Salary"
            && x.Kind == BudgetItemKind.Funding));
        Assert.False(await db.ProcessedProjectionEvents.AnyAsync(x => x.EventId == eventId));
    }

    [Fact]
    public async Task ProjectionEnvelopeProcessingIsIdempotentForDuplicateEvents()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary", BudgetItemKind.Funding);
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var envelopeJson = (await db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.EventType == "budgetytzar.budgeting.budget-adjustment-recorded.v1")
            .SingleAsync())
            .EnvelopeJson;
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionConsumerService>();

        await projector.ProjectEnvelope(envelopeJson, CancellationToken.None);
        await projector.ProjectEnvelope(envelopeJson, CancellationToken.None);

        Assert.Equal(1, await db.ProcessedProjectionEvents.CountAsync(x => x.Status == ProjectionProcessingStatus.Completed));
        Assert.Equal(1, await db.BudgetSnapshotProjections.CountAsync(x => x.BudgetId == budget.Id && x.Date == new DateOnly(2026, 6, 10)));
    }

    [Fact]
    public async Task ProjectionEnvelopeProcessingCompletesOnlyTheConsumedProjectionEvent()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary", BudgetItemKind.Funding);
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var consumedMessage = await db.OutboxMessages
            .AsNoTracking()
            .SingleAsync(x => x.EventType == "budgetytzar.budgeting.budget-adjustment-recorded.v1");
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionConsumerService>();

        await projector.ProjectEnvelope(consumedMessage.EnvelopeJson, CancellationToken.None);

        var outbox = await db.OutboxMessages.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync();
        Assert.All(outbox, x => Assert.Null(x.ProjectedAt));

        var projectionEvent = await db.ProcessedProjectionEvents.AsNoTracking().SingleAsync(x => x.BudgetId == budget.Id);
        Assert.Equal(consumedMessage.Id, projectionEvent.EventId);
        Assert.Equal(ProjectionProcessingStatus.Completed, projectionEvent.Status);
    }

    [Fact]
    public async Task DeltaProjectionProcessingUpdatesProjectionStateWithoutImplicitOutboxReplay()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary", BudgetItemKind.Funding);
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var messages = (await db.OutboxMessages.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync())
            .OrderBy(x => x.CreatedAt)
            .ToList();
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionConsumerService>();

        foreach (var message in messages)
        {
            await projector.ProjectEnvelope(message.EnvelopeJson, CancellationToken.None);
        }

        Assert.Equal(messages.Count, await db.ProcessedProjectionEvents.CountAsync(x => x.BudgetId == budget.Id && x.Status == ProjectionProcessingStatus.Completed));
        Assert.Single(await db.BudgetItemProjectionStates.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync());
        Assert.Single(await db.BudgetAdjustmentProjectionStates.AsNoTracking().Where(x => x.BudgetId == budget.Id).ToListAsync());
        Assert.True(await db.BudgetSnapshotItemProjections.AnyAsync(x =>
            x.BudgetId == budget.Id
            && x.BudgetItemId == salary.Id
            && x.Kind == BudgetItemKind.Funding
            && x.Balance == -100m));
    }

    [Fact]
    public async Task CommandsDoNotCreateProjectionProcessingRows()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var budget = await BudgetApiTestClient.CreateBudget(client);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var outbox = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.BudgetId == budget.Id);

        Assert.Equal(budget.Id, outbox.BudgetId);
        Assert.False(await db.ProcessedProjectionEvents.AnyAsync(x => x.BudgetId == budget.Id));
    }

    [Fact]
    public async Task ProjectionEnvelopeProcessingSkipsEventsActivelyClaimedByAnotherInstance()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.BudgetId == budget.Id);
        var now = DateTimeOffset.UtcNow;
        db.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = message.Id,
            EventType = message.EventType,
            BudgetId = budget.Id,
            OccurredAt = message.CreatedAt,
            ProcessedAt = now,
            Status = ProjectionProcessingStatus.Processing,
            ProcessingInstanceId = Guid.NewGuid(),
            ProcessingStartedAt = now,
            ProcessingUpdatedAt = now
        });
        await db.SaveChangesAsync();
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionConsumerService>();

        var projected = await projector.ProjectEnvelope(message.EnvelopeJson, CancellationToken.None);

        Assert.False(projected);
        Assert.Equal(ProjectionProcessingStatus.Processing, await db.ProcessedProjectionEvents
            .Where(x => x.EventId == message.Id)
            .Select(x => x.Status)
            .SingleAsync());
    }

    [Fact]
    public async Task ProjectionEnvelopeProcessingReclaimsStaleProcessingRows()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.BudgetId == budget.Id);
        var staleAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        db.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = message.Id,
            EventType = message.EventType,
            BudgetId = budget.Id,
            OccurredAt = message.CreatedAt,
            ProcessedAt = staleAt,
            Status = ProjectionProcessingStatus.Processing,
            ProcessingInstanceId = Guid.NewGuid(),
            ProcessingStartedAt = staleAt,
            ProcessingUpdatedAt = staleAt
        });
        await db.SaveChangesAsync();
        var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionConsumerService>();

        var projected = await projector.ProjectEnvelope(message.EnvelopeJson, CancellationToken.None);

        Assert.True(projected);
        db.ChangeTracker.Clear();
        Assert.Equal(ProjectionProcessingStatus.Completed, await db.ProcessedProjectionEvents
            .Where(x => x.EventId == message.Id)
            .Select(x => x.Status)
            .SingleAsync());
    }
}
