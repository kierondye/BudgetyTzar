using BudgetyTzar.Api.Application.Reporting;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapReportEndpoints(RouteGroupBuilder budgets)
    {
        MapGetBudgetSnapshotEndpoint(budgets);
        MapListAuditEventsEndpoint(budgets);
        MapGetProjectionStatusEndpoint(budgets);
        MapGetProjectionEventsEndpoint(budgets);
    }
}
