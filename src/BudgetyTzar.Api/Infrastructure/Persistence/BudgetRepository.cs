using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Infrastructure.Persistence;

public sealed class BudgetRepository(BudgetDbContext db) : IBudgetRepository
{
    public async Task<BudgetLoadResult> GetBudgetWithItems(Guid budgetId, CancellationToken ct)
    {
        var budget = await db.Budgets
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == budgetId, ct);
        if (budget is null)
        {
            return new BudgetLoadResult.BudgetNotFound();
        }

        var budgetItems = await db.BudgetItems
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .ToListAsync(ct);

        return new BudgetLoadResult.Success(budget.WithItems(budgetItems));
    }
}
