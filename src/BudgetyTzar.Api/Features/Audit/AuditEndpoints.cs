namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapAuditEndpoints(RouteGroupBuilder budgets)
    {
        MapListAuditEventsEndpoint(budgets);
    }
}
