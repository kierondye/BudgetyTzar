using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapListAuditEventsEndpoint(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/audit-events", async (
            Guid budgetId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            Guid? waitForEventId,
            BudgetDbContext db,
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
    }
}
