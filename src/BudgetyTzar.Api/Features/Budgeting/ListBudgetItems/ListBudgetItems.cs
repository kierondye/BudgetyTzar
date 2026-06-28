using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapListBudgetItemsEndpoint(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/budget-items", async (Guid budgetId, BudgetDbContext db, CancellationToken ct) =>
        {
            if (!await BudgetExistsForEndpoint(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var items = await db.BudgetItems
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId)
                .OrderBy(x => x.Name)
                .Select(x => new BudgetItemDto(x.Id, x.BudgetId, x.Name, x.Kind, x.IsArchived, x.ArchivedAt, x.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(items);
        });
    }
}
