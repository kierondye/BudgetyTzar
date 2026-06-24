using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Threading.Channels;

namespace BudgetyTzar.Api.Features;

public sealed record ProjectionStatusResponse(Guid BudgetId, Guid EventId, string Status);
public sealed record ProjectionPendingResponse(
    Guid BudgetId,
    Guid EventId,
    string Status,
    string StatusUrl,
    string EventStreamUrl);

public static partial class Endpoints
{
    private static void MapReportEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/snapshot", async (
            Guid budgetId,
            DateOnly date,
            Guid? waitForEventId,
            BudgetDbContext db,
            IOptions<ProjectionOptions> projections,
            CancellationToken ct) =>
        {
            if (projections.Value.UseProjectionBackedReports
                && await GetPendingProjectionResponse(db, budgetId, waitForEventId, ct) is { } pending)
            {
                return Results.Accepted(pending.StatusUrl, pending);
            }

            return await LedgerSnapshotCalculator.GetProjectedOrCalculate(db, budgetId, date, projections.Value.UseProjectionBackedReports, ct) is { } snapshot
                ? Results.Ok(snapshot)
                : Results.NotFound();
        });

        budgets.MapGet("/{budgetId:guid}/audit-events", async (
            Guid budgetId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            Guid? waitForEventId,
            BudgetDbContext db,
            IOptions<ProjectionOptions> projections,
            CancellationToken ct) =>
        {
            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var query = db.AuditEvents
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId);
            if (from.HasValue)
            {
                query = query.Where(x => x.OccurredAt >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(x => x.OccurredAt <= to.Value);
            }

            var auditEvents = await query.ToListAsync(ct);
            var events = auditEvents
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Id)
                .Select(x => new AuditEventDto(
                    x.Id,
                    x.BudgetId,
                    x.OccurredAt,
                    x.EventType,
                    x.EntityType,
                    x.EntityId,
                    x.Description,
                    x.Details))
                .ToList();

            return Results.Ok(events);
        });

        budgets.MapGet("/{budgetId:guid}/projections/status", async (
            Guid budgetId,
            Guid eventId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var status = await GetProjectionStatus(db, budgetId, eventId, ct);
            return Results.Ok(new ProjectionStatusResponse(budgetId, eventId, status));
        });

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

    private static async Task<ProjectionPendingResponse?> GetPendingProjectionResponse(
        BudgetDbContext db,
        Guid budgetId,
        Guid? waitForEventId,
        CancellationToken ct)
    {
        if (!await BudgetExists(db, budgetId, ct))
        {
            return null;
        }

        var eventId = waitForEventId;
        if (!eventId.HasValue)
        {
            var projectionEvents = await db.ProcessedProjectionEvents
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId)
                .Select(x => new { x.EventId, x.OccurredAt, x.ProcessedAt })
                .ToListAsync(ct);
            eventId = projectionEvents
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.ProcessedAt)
                .Select(x => (Guid?)x.EventId)
                .FirstOrDefault();
        }
        if (!eventId.HasValue)
        {
            return null;
        }

        var status = await GetProjectionStatus(db, budgetId, eventId.Value, ct);
        if (status == "ready" || status == "unknown")
        {
            return null;
        }

        var statusUrl = $"/api/budgets/{budgetId}/projections/status?eventId={eventId.Value}";
        return new ProjectionPendingResponse(
            budgetId,
            eventId.Value,
            status,
            statusUrl,
            $"/api/budgets/{budgetId}/projection-events?eventId={eventId.Value}");
    }

    private static async Task<string> GetProjectionStatus(BudgetDbContext db, Guid budgetId, Guid eventId, CancellationToken ct)
    {
        var projectionEvent = await db.ProcessedProjectionEvents
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => new { x.BudgetId, x.Status })
            .FirstOrDefaultAsync(ct);
        if (projectionEvent is null || projectionEvent.BudgetId != budgetId)
        {
            return "unknown";
        }

        if (projectionEvent.Status == ProjectionProcessingStatus.Completed)
        {
            return "ready";
        }

        return "pending";
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
