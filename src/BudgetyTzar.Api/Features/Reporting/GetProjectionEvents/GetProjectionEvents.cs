using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Threading.Channels;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapGetProjectionEventsEndpoint(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/projection-events", async (
            Guid budgetId,
            Guid? eventId,
            ProjectionNotificationService notifications,
            BudgetDbContext db,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";
            httpContext.Response.ContentType = "text/event-stream";

            CancellationTokenSource? oneShotSubscription = null;
            var readerCancellationToken = ct;
            if (eventId.HasValue)
            {
                oneShotSubscription = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readerCancellationToken = oneShotSubscription.Token;
            }

            var reader = notifications.Subscribe(readerCancellationToken);
            try
            {
                if (eventId.HasValue)
                {
                    await WriteWhenProjectionEventIsReady(budgetId, eventId.Value, reader, db, httpContext, ct);
                    return;
                }

                await foreach (var notification in reader.ReadAllAsync(ct))
                {
                    if (notification.BudgetId != budgetId)
                    {
                        continue;
                    }

                    if (eventId.HasValue && notification.EventId != eventId.Value)
                    {
                        continue;
                    }

                    await WriteProjectionReadyEvent(httpContext, notification, ct);
                    if (eventId.HasValue)
                    {
                        return;
                    }
                }
            }
            finally
            {
                oneShotSubscription?.Cancel();
                oneShotSubscription?.Dispose();
            }
        });
    }

    private static async Task WriteWhenProjectionEventIsReady(
        Guid budgetId,
        Guid eventId,
        ChannelReader<ProjectionReadyNotification> reader,
        BudgetDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!await BudgetExists(db, budgetId, ct))
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            var status = await GetProjectionStatus(db, budgetId, eventId, ct);
            if (status == "ready")
            {
                await WriteProjectionReadyEvent(httpContext, await CreateProjectionReadyNotification(db, budgetId, eventId, ct), ct);
                return;
            }

            while (reader.TryRead(out var notification))
            {
                if (notification.BudgetId == budgetId && notification.EventId == eventId)
                {
                    await WriteProjectionReadyEvent(httpContext, notification, ct);
                    return;
                }
            }

            await Task.Delay(250, ct);
        }
    }

    private static async Task<ProjectionReadyNotification> CreateProjectionReadyNotification(
        BudgetDbContext db,
        Guid budgetId,
        Guid eventId,
        CancellationToken ct)
    {
        var projected = await db.ProcessedProjectionEvents
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.BudgetId == budgetId && x.Status == ProjectionProcessingStatus.Completed)
            .Select(x => new { x.EventType, x.CompletedAt, x.ProcessedAt })
            .SingleAsync(ct);

        return new ProjectionReadyNotification(
            budgetId,
            eventId,
            projected.EventType,
            projected.CompletedAt ?? projected.ProcessedAt,
            ["snapshot"]);
    }

    private static async Task WriteProjectionReadyEvent(
        HttpContext httpContext,
        ProjectionReadyNotification notification,
        CancellationToken ct)
    {
        await httpContext.Response.WriteAsync("event: projection-ready\n", ct);
        await httpContext.Response.WriteAsync($"data: {JsonSerializer.Serialize(notification, EventSerialization.Options)}\n\n", ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }
}
