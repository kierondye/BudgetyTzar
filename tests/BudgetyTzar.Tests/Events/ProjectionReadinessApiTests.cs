using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Features;
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
        var salary = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Salary");
        await BudgetApiTestClient.RecordAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 10));

        using (var scope = app.Services.CreateScope())
        {
            var purgeDb = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            await purgeDb.BudgetAdjustments.ExecuteDeleteAsync();
            await purgeDb.BudgetItems.ExecuteDeleteAsync();
        }

        var response = await client.GetAsync($"/api/budgets/{budget.Id}/snapshot?date=2026-06-10");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var verifyScope = app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        Assert.False(await db.BudgetSnapshotProjections.AnyAsync(x => x.BudgetId == budget.Id));
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.BudgetId == budget.Id && x.ProjectedAt != null));
    }

    [Fact]
    public async Task ProjectionBackedSnapshotReturnsAcceptedWhenProjectionRowsAreMissing()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);

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
                ["snapshot", "auditTimeline"]));
            await Task.Delay(50);
        }

        var body = await streamTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("event: projection-ready", body);
        Assert.Contains(eventId.ToString(), body);
    }

    [Fact]
    public async Task ProjectionBackedAuditEndpointDoesNotRebuildFromOutboxAtReadTime()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);

        var response = await client.GetAsync($"/api/budgets/{budget.Id}/audit-events");
        var pending = await response.Content.ReadFromJsonAsync<ProjectionPendingResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("pending", pending!.Status);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        Assert.False(await db.BudgetAuditTimelines.AnyAsync(x => x.BudgetId == budget.Id));
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.BudgetId == budget.Id && x.ProjectedAt != null));
    }

    [Fact]
    public async Task ProjectionStatusTransitionsFromPendingToReady()
    {
        await using var app = new BudgetApiFactory(useProjectionBackedReports: true);
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();

        var createResponse = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        createResponse.EnsureSuccessStatusCode();
        var budget = (await createResponse.Content.ReadFromJsonAsync<Budget>())!;
        var eventId = Guid.Parse(Assert.Single(createResponse.Headers.GetValues(CommandResultHttpExtensions.EventIdHeaderName)));

        var pending = await client.GetFromJsonAsync<ProjectionStatusResponse>(
            $"/api/budgets/{budget.Id}/projections/status?eventId={eventId}");
        Assert.Equal("pending", pending!.Status);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
            var projector = scope.ServiceProvider.GetRequiredService<ReportingProjectionService>();
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
}
