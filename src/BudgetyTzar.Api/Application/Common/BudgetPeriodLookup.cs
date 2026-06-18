using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Common;

internal static class BudgetPeriodLookup
{
    public static async Task<Guid?> FindPeriodIdForDate(
        BudgetDbContext db,
        Guid budgetId,
        DateOnly date,
        CancellationToken ct) =>
        (await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.StartDate <= date && x.EndDate >= date, ct))?.Id;
}
