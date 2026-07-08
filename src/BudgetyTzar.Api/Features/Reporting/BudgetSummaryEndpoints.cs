using System.Diagnostics;
using BudgetyTzar.Api.Observability;

namespace BudgetyTzar.Api.Features.Reporting;

public static class BudgetSummaryEndpoints
{
    public static IServiceCollection AddReporting(this IServiceCollection services)
    {
        services.AddSingleton<BudgetSummaryService>();
        return services;
    }

    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var budgets = endpoints.MapGroup("/api/budgets")
            .WithTags("Reporting");

        budgets.MapGet("/{budgetId:guid}/summary", GetBudgetSummary)
            .WithName("GetBudgetSummary");

        return endpoints;
    }

    private static IResult GetBudgetSummary(
        Guid budgetId,
        BudgetSummaryService service,
        BudgetyTzarTelemetry telemetry)
    {
        using var activity = BudgetyTzarTelemetry.StartBudgetSummaryQuery();
        var started = Stopwatch.GetTimestamp();
        var outcome = "error";

        try
        {
            var result = service.Get(budgetId);
            outcome = result is GetBudgetSummaryResult.Found ? "found" : "not_found";
            activity?.SetTag("query.outcome", outcome);

            return result switch
            {
                GetBudgetSummaryResult.NotFound => Results.NotFound(),
                GetBudgetSummaryResult.Found found => Results.Ok(BudgetSummaryResponse.FromSummary(found.Summary)),
                _ => throw new InvalidOperationException("Unexpected budget summary result.")
            };
        }
        finally
        {
            telemetry.RecordBudgetSummary(
                Stopwatch.GetElapsedTime(started).TotalSeconds,
                outcome);
        }
    }
}
