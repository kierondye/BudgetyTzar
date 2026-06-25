using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapGetTransactionEndpoint(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/transactions/{transactionId:guid}", async (
            Guid budgetId,
            Guid transactionId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var transaction = await db.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
            if (transaction is null)
            {
                return Results.NotFound();
            }

            var allocations = await db.TransactionAllocations
                .AsNoTracking()
                .Where(x => x.TransactionId == transactionId)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);
            return Results.Ok(new TransactionDetail(transaction, allocations));
        });
    }
}
