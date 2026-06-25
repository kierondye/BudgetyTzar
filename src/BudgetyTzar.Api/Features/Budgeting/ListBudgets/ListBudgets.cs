using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapListBudgetsEndpoint(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/", async (BudgetDbContext db, CancellationToken ct) =>
            await db.Budgets.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct));
    }
}
