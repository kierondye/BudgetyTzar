namespace BudgetyTzar.Api.Features.Reporting;

public static class BudgetSummaryEndpoints
{
    public static IServiceCollection AddReporting(this IServiceCollection services)
    {
        services.AddScoped<BudgetSummaryService>();
        return services;
    }

    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var budgets = endpoints.MapGroup("/api/budgets")
            .WithTags("Reporting")
            .RequireAuthorization();

        budgets.MapGet("/{budgetId:guid}/summary", GetBudgetSummary)
            .WithName("GetBudgetSummary");

        return endpoints;
    }

    private static IResult GetBudgetSummary(Guid budgetId, BudgetSummaryService service)
    {
        var result = service.Get(budgetId);

        return result switch
        {
            GetBudgetSummaryResult.NotFound => Results.NotFound(),
            GetBudgetSummaryResult.Found found => Results.Ok(BudgetSummaryResponse.FromSummary(found.Summary)),
            _ => throw new InvalidOperationException("Unexpected budget summary result.")
        };
    }
}
