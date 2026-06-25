namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapReportEndpoints(RouteGroupBuilder budgets)
    {
        MapGetBudgetSnapshotEndpoint(budgets);
        MapGetProjectionStatusEndpoint(budgets);
        MapGetProjectionEventsEndpoint(budgets);
    }
}
