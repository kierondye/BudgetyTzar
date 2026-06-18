using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Persistence;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapReportEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/snapshot", async (
            Guid budgetId,
            DateOnly date,
            BudgetDbContext db,
            CancellationToken ct) =>
            await LedgerSnapshotCalculator.Calculate(db, budgetId, date, ct) is { } snapshot
                ? Results.Ok(snapshot)
                : Results.NotFound());
    }
}
