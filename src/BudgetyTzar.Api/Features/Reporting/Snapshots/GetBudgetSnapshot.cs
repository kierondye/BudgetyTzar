using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Features;

public sealed record ProjectionPendingResponse(
    Guid BudgetId,
    Guid EventId,
    string Status,
    string StatusUrl,
    string EventStreamUrl);

public static partial class Endpoints
{
    private static void MapGetBudgetSnapshotEndpoint(RouteGroupBuilder budgets)
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
}
