using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapGetBudgetEndpoint(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}", async (Guid budgetId, BudgetDbContext db, CancellationToken ct) =>
            await db.Budgets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == budgetId, ct) is { } budget
                ? Results.Ok(budget)
                : Results.NotFound());
    }
}
