using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapReportEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/snapshot", async (
            Guid budgetId,
            DateOnly date,
            BudgetDbContext db,
            IOptions<ProjectionOptions> projections,
            ReportingProjectionService projector,
            CancellationToken ct) =>
        {
            if (projections.Value.UseProjectionBackedReports)
            {
                await projector.RebuildBudget(budgetId, ct);
            }

            return await LedgerSnapshotCalculator.GetProjectedOrCalculate(db, budgetId, date, projections.Value.UseProjectionBackedReports, ct) is { } snapshot
                ? Results.Ok(snapshot)
                : Results.NotFound();
        });

        budgets.MapGet("/{budgetId:guid}/audit-events", async (
            Guid budgetId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            BudgetDbContext db,
            IOptions<ProjectionOptions> projections,
            ReportingProjectionService projector,
            CancellationToken ct) =>
        {
            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            if (projections.Value.UseProjectionBackedReports)
            {
                await projector.RebuildBudget(budgetId, ct);

                var projectedQuery = db.BudgetAuditTimelines
                    .AsNoTracking()
                    .Where(x => x.BudgetId == budgetId);
                if (from.HasValue)
                {
                    projectedQuery = projectedQuery.Where(x => x.OccurredAt >= from.Value);
                }

                if (to.HasValue)
                {
                    projectedQuery = projectedQuery.Where(x => x.OccurredAt <= to.Value);
                }

                var projectedEvents = await projectedQuery.ToListAsync(ct);
                return Results.Ok(projectedEvents
                    .OrderByDescending(x => x.OccurredAt)
                    .ThenByDescending(x => x.AuditEventId)
                    .Select(x => new AuditEventDto(
                        x.AuditEventId,
                        x.BudgetId,
                        x.OccurredAt,
                        x.EventType,
                        x.EntityType,
                        x.EntityId,
                        x.Description,
                        x.Details))
                    .ToList());
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
