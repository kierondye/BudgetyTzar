using BudgetyTzar.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static Task<bool> BudgetExists(BudgetDbContext db, Guid budgetId, CancellationToken ct) =>
        db.Budgets.AnyAsync(x => x.Id == budgetId, ct);

    private static Task<bool> PeriodBelongsToBudget(BudgetDbContext db, Guid budgetId, Guid periodId, CancellationToken ct) =>
        db.BudgetPeriods.AnyAsync(x => x.Id == periodId && x.BudgetId == budgetId, ct);

    private static async Task<Guid?> FindPeriodIdForDate(BudgetDbContext db, Guid budgetId, DateOnly date, CancellationToken ct) =>
        (await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.StartDate <= date && x.EndDate >= date, ct))?.Id;
}
