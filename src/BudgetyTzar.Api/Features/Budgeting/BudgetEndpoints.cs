using FluentValidation;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    public static RouteGroupBuilder MapBudgetEndpoints(this RouteGroupBuilder api)
    {
        var budgets = api.MapGroup("/budgets").WithTags("Budgets");

        MapListBudgetsEndpoint(budgets);
        MapGetBudgetEndpoint(budgets);
        budgets.MapPost("/", async (
            CreateBudgetRequest request,
            IValidator<CreateBudgetRequest> validator,
            CreateBudgetHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (await validator.ValidateEndpointRequest(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(request.Name, request.Currency, ct);
            return result.ToHttpResult(httpContext, budget => Results.Created($"/api/budgets/{budget.Id}", budget));
        });

        MapBudgetItemEndpoints(budgets);
        MapTransactionEndpoints(budgets);
        MapReallocationEndpoints(budgets);
        MapAdjustmentEndpoints(budgets);
        MapReportEndpoints(budgets);
        MapAuditEndpoints(budgets);

        return api;
    }
}
