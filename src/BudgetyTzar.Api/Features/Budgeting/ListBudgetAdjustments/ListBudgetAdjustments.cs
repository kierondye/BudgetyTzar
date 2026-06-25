using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapListBudgetAdjustmentsEndpoint(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/budget-items/{budgetItemId:guid}/adjustments", async (
            Guid budgetId,
            Guid budgetItemId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.BudgetItems.AnyAsync(x => x.BudgetId == budgetId && x.Id == budgetItemId, ct))
            {
                return Results.NotFound();
            }

            var adjustments = await db.BudgetAdjustments
                .AsNoTracking()
                .Where(x => x.BudgetItemId == budgetItemId && (x.BudgetId == budgetId || x.BudgetId == Guid.Empty))
                .ToListAsync(ct);
            var dtos = adjustments
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.CreatedAt)
                .Select(x => new BudgetAdjustmentDto(
                    x.Id,
                    x.BudgetId == Guid.Empty ? budgetId : x.BudgetId,
                    x.BudgetItemId,
                    x.ReallocationId,
                    x.Date,
                    x.Amount,
                    x.Type,
                    x.Notes,
                    x.CreatedAt))
                .ToList();
            return Results.Ok(dtos);
        });
    }
}
