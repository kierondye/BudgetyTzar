using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Budgeting;

public sealed class BudgetLineEligibilityService(BudgetDbContext db)
{
    public async Task<List<BudgetLine>> GetEligibleBudgetLines(
        Guid budgetId,
        Guid? periodId,
        IReadOnlyCollection<Guid> lineIds,
        CancellationToken ct)
    {
        var lines = await db.BudgetLines
            .AsNoTracking()
            .Where(x => lineIds.Contains(x.Id) && x.BudgetId == budgetId)
            .ToListAsync(ct);

        var archivedLines = lines.Where(x => x.IsArchived).ToList();
        if (archivedLines.Count == 0)
        {
            return lines;
        }

        if (!periodId.HasValue)
        {
            return lines.Where(x => !x.IsArchived).ToList();
        }

        var summary = await DashboardQueries.GetPeriodSummary(db, budgetId, periodId.Value, ct);
        var activeArchivedLineIds = summary?.Lines
            .Where(x => x.IsArchived)
            .Select(x => x.BudgetLineId)
            .ToHashSet() ?? [];

        return lines
            .Where(x => !x.IsArchived || activeArchivedLineIds.Contains(x.Id))
            .ToList();
    }

    public async Task<BudgetLine?> GetEligibleBudgetLine(
        Guid budgetId,
        Guid? periodId,
        Guid lineId,
        CancellationToken ct) =>
        (await GetEligibleBudgetLines(budgetId, periodId, [lineId], ct)).SingleOrDefault();
}
