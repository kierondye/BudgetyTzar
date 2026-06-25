using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record ProjectionStatusResponse(Guid BudgetId, Guid EventId, string Status);

public static partial class Endpoints
{
    private static void MapGetProjectionStatusEndpoint(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/projections/status", async (
            Guid budgetId,
            Guid eventId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var status = await GetProjectionStatus(db, budgetId, eventId, ct);
            return Results.Ok(new ProjectionStatusResponse(budgetId, eventId, status));
        });
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
}
