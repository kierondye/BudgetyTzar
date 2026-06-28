using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests;

public sealed class ProjectionReadinessApiTests
{
    [Fact]
    public async Task CommandResponsesExposeProjectionReadinessHeaders()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        response.EnsureSuccessStatusCode();
        var budget = (await response.Content.ReadFromJsonAsync<Budget>())!;
        var eventId = Assert.Single(response.Headers.GetValues(CommandResultHttpExtensions.EventIdHeaderName));
        var statusUrl = Assert.Single(response.Headers.GetValues(CommandResultHttpExtensions.ProjectionStatusHeaderName));

        Assert.True(Guid.TryParse(eventId, out _));
        Assert.Equal($"/api/budgets/{budget.Id}/projections/status?eventId={eventId}", statusUrl);
    }

    [Fact]
    public async Task ProjectionBackedSnapshotEndpointDoesNotRebuildFromOutboxAtReadTime()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary", BudgetItemKind.Funding);
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using (var scope = app.Services.CreateScope())
        {
            var purgeDb = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            await purgeDb.BudgetAdjustments.ExecuteDeleteAsync();
            await purgeDb.BudgetItems.ExecuteDeleteAsync();
        }

        var response = await client.GetAsync($"/api/budgets/{budget.Id}/snapshot?date=2026-06-10");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var verifyScope = app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        Assert.False(await db.BudgetSnapshotProjections.AnyAsync(x => x.BudgetId == budget.Id));
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.BudgetId == budget.Id && x.ProjectedAt != null));
    }

    [Fact]
    public async Task ProjectionBackedSnapshotReturnsAcceptedWhenProjectionRowIsPending()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            var outbox = await db.OutboxMessages.SingleAsync(x => x.BudgetId == budget.Id);
            var now = DateTimeOffset.UtcNow;
            db.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
            {
                EventId = outbox.Id,
                EventType = outbox.EventType,
                BudgetId = budget.Id,
                OccurredAt = outbox.CreatedAt,
                ProcessedAt = now,
                Status = ProjectionProcessingStatus.Processing,
                ProcessingInstanceId = Guid.NewGuid(),
                ProcessingStartedAt = now,
                ProcessingUpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/budgets/{budget.Id}/snapshot?date=2026-06-30");
        var pending = await response.Content.ReadFromJsonAsync<ProjectionPendingResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("pending", pending!.Status);
        Assert.Equal($"/api/budgets/{budget.Id}/projection-events?eventId={pending.EventId}", pending.EventStreamUrl);
    }

    [Fact]
    public async Task ProjectionEventStreamForPendingEventCompletesWhenMatchingNotificationArrives()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        response.EnsureSuccessStatusCode();
        var budget = (await response.Content.ReadFromJsonAsync<Budget>())!;
        var eventId = Guid.Parse(Assert.Single(response.Headers.GetValues(CommandResultHttpExtensions.EventIdHeaderName)));

        var streamTask = client.GetStringAsync($"/api/budgets/{budget.Id}/projection-events?eventId={eventId}");
        var notifications = app.Services.GetRequiredService<ProjectionNotificationService>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!streamTask.IsCompleted && DateTimeOffset.UtcNow < deadline)
        {
            notifications.Publish(new ProjectionReadyNotification(
                budget.Id,
                eventId,
                "budgetytzar.budgeting.budget-created.v1",
                DateTimeOffset.UtcNow,
                ["snapshot"]));
            await Task.Delay(50);
        }

        var body = await streamTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("event: projection-ready", body);
        Assert.Contains(eventId.ToString(), body);
    }

    [Fact]
    public async Task ProjectionBackedAuditEndpointReadsDurableAuditEventsAfterAuditProjectionEvenWhenReportingProjectionIsPending()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        await app.ProjectAuditEventsAsync(budget.Id);

        var response = await client.GetAsync($"/api/budgets/{budget.Id}/audit-events");
        var auditEvents = await response.Content.ReadFromJsonAsync<IReadOnlyList<AuditEventDto>>();

        response.EnsureSuccessStatusCode();
        Assert.Contains(auditEvents!, x => x.EventType == "BudgetCreated");

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.BudgetId == budget.Id && x.ProjectedAt != null));
    }

    [Fact]
    public async Task ProjectionStatusTransitionsFromUnknownToPendingToReady()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var createResponse = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        createResponse.EnsureSuccessStatusCode();
        var budget = (await createResponse.Content.ReadFromJsonAsync<Budget>())!;
        var eventId = Guid.Parse(Assert.Single(createResponse.Headers.GetValues(CommandResultHttpExtensions.EventIdHeaderName)));

        var unknownBeforeConsumer = await client.GetFromJsonAsync<ProjectionStatusResponse>(
            $"/api/budgets/{budget.Id}/projections/status?eventId={eventId}");
        Assert.Equal("unknown", unknownBeforeConsumer!.Status);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            var outbox = await db.OutboxMessages.SingleAsync(x => x.Id == eventId);
            var staleAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            db.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
            {
                EventId = outbox.Id,
                EventType = outbox.EventType,
                BudgetId = budget.Id,
                OccurredAt = outbox.CreatedAt,
                ProcessedAt = staleAt,
                Status = ProjectionProcessingStatus.Processing,
                ProcessingInstanceId = Guid.NewGuid(),
                ProcessingStartedAt = staleAt,
                ProcessingUpdatedAt = staleAt
            });
            await db.SaveChangesAsync();
        }

        var pending = await client.GetFromJsonAsync<ProjectionStatusResponse>(
            $"/api/budgets/{budget.Id}/projections/status?eventId={eventId}");
        Assert.Equal("pending", pending!.Status);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionConsumerService>();
            var envelopeJson = await db.OutboxMessages
                .Where(x => x.Id == eventId)
                .Select(x => x.EnvelopeJson)
                .SingleAsync();
            await projector.ProjectEnvelope(envelopeJson, CancellationToken.None);
        }

        var ready = await client.GetFromJsonAsync<ProjectionStatusResponse>(
            $"/api/budgets/{budget.Id}/projections/status?eventId={eventId}");
        var unknown = await client.GetFromJsonAsync<ProjectionStatusResponse>(
            $"/api/budgets/{Guid.NewGuid()}/projections/status?eventId={eventId}");

        Assert.Equal("ready", ready!.Status);
        Assert.Equal("unknown", unknown!.Status);
    }

    [Fact]
    public async Task ProjectionStatusIgnoresOutboxProjectedAt()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var createResponse = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        createResponse.EnsureSuccessStatusCode();
        var budget = (await createResponse.Content.ReadFromJsonAsync<Budget>())!;
        var eventId = Guid.Parse(Assert.Single(createResponse.Headers.GetValues(CommandResultHttpExtensions.EventIdHeaderName)));

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            var outbox = await db.OutboxMessages.SingleAsync(x => x.Id == eventId);
            outbox.ProjectedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var status = await client.GetFromJsonAsync<ProjectionStatusResponse>(
            $"/api/budgets/{budget.Id}/projections/status?eventId={eventId}");

        Assert.Equal("unknown", status!.Status);
    }
}
