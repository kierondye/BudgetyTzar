using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Budgeting;

public sealed class BudgetLineEligibilityService(BudgetDbContext db)
{
    public async Task<List<BudgetLine>> GetEligibleBudgetLines(
        Guid budgetId,
        IReadOnlyCollection<Guid> lineIds,
        CancellationToken ct)
        => await db.BudgetLines
            .AsNoTracking()
            .Where(x => lineIds.Contains(x.Id) && x.BudgetId == budgetId)
            .ToListAsync(ct);

    public async Task<BudgetLine?> GetEligibleBudgetLine(
        Guid budgetId,
        Guid lineId,
        CancellationToken ct) =>
        (await GetEligibleBudgetLines(budgetId, [lineId], ct)).SingleOrDefault();
}
