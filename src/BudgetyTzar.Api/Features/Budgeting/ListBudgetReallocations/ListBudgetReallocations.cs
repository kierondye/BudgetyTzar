using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapListBudgetReallocationsEndpoint(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/reallocations", async (
            Guid budgetId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var reallocations = await db.BudgetReallocations
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId)
                .ToListAsync(ct);
            reallocations = reallocations
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.CreatedAt)
                .ToList();
            var reallocationIds = reallocations.Select(x => x.Id).ToArray();
            var adjustments = await db.BudgetAdjustments
                .AsNoTracking()
                .Where(x => x.ReallocationId.HasValue && reallocationIds.Contains(x.ReallocationId.Value))
                .GroupBy(x => x.ReallocationId!.Value)
                .ToDictionaryAsync(
                    x => x.Key,
                    x => (IReadOnlyList<BudgetReallocationAdjustmentItem>)x
                        .Select(y => new BudgetReallocationAdjustmentItem(y.BudgetItemId, y.Amount, y.Type))
                        .ToList(),
                    ct);

            return Results.Ok(reallocations.Select(x => new BudgetReallocationDto(
                x.Id,
                x.BudgetId,
                x.Date,
                x.Notes,
                adjustments.GetValueOrDefault(x.Id, []),
                x.CreatedAt)).ToList());
        });
    }
}
