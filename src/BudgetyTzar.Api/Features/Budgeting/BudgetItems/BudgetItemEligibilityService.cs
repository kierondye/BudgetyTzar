using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed class BudgetItemEligibilityService(BudgetDbContext db)
{
    public async Task<List<BudgetItem>> GetBudgetItems(
        Guid budgetId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken ct)
        => await db.BudgetItems
            .AsNoTracking()
            .Where(x => itemIds.Contains(x.Id) && x.BudgetId == budgetId)
            .ToListAsync(ct);

    public async Task<BudgetItem?> GetBudgetItem(
        Guid budgetId,
        Guid itemId,
        CancellationToken ct) =>
        (await GetBudgetItems(budgetId, [itemId], ct)).SingleOrDefault();
}
