using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapListTransactionsEndpoint(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/transactions", async (
            Guid budgetId,
            DateOnly? from,
            DateOnly? to,
            string? allocationStatus,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await BudgetExistsForEndpoint(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var query = db.Transactions.AsNoTracking().Where(x => x.BudgetId == budgetId);
            if (from.HasValue)
            {
                query = query.Where(x => x.TransactionDate >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(x => x.TransactionDate <= to.Value);
            }

            TransactionAllocationStatus? parsedAllocationStatus = null;
            if (!string.IsNullOrWhiteSpace(allocationStatus))
            {
                if (!Enum.TryParse<TransactionAllocationStatus>(allocationStatus, ignoreCase: true, out var parsed))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(allocationStatus)] = ["Allocation status must be unallocated, partiallyAllocated, or fullyAllocated."]
                    });
                }

                parsedAllocationStatus = parsed;
            }

            var transactions = await query.OrderByDescending(x => x.TransactionDate).ToListAsync(ct);
            if (!parsedAllocationStatus.HasValue)
            {
                return Results.Ok(transactions);
            }

            var transactionIds = transactions.Select(x => x.Id).ToArray();
            var allocationTotals = await db.TransactionAllocations
                .AsNoTracking()
                .Where(x => transactionIds.Contains(x.TransactionId))
                .GroupBy(x => x.TransactionId)
                .Select(x => new { TransactionId = x.Key, Amount = x.Sum(y => y.Amount) })
                .ToDictionaryAsync(x => x.TransactionId, x => x.Amount, ct);

            var filtered = transactions
                .Where(x => x.GetAllocationStatus(allocationTotals.GetValueOrDefault(x.Id)) == parsedAllocationStatus.Value)
                .ToList();
            return Results.Ok(filtered);
        });
    }
}
