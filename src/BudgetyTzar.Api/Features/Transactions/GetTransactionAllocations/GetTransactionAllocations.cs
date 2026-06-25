using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapGetTransactionAllocationsEndpoint(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/transactions/{transactionId:guid}/allocations", async (
            Guid budgetId,
            Guid transactionId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.Transactions.AnyAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct))
            {
                return Results.NotFound();
            }

            var allocations = await db.TransactionAllocations
                .AsNoTracking()
                .Where(x => x.TransactionId == transactionId)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);
            return Results.Ok(allocations);
        });
    }
}
