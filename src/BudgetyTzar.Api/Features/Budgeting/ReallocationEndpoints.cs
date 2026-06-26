using FluentValidation;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapReallocationEndpoints(RouteGroupBuilder budgets)
    {
        MapListBudgetReallocationsEndpoint(budgets);
        budgets.MapPost("/{budgetId:guid}/reallocations", async (
            Guid budgetId,
            CreateBudgetItemReallocationRequest request,
            IValidator<CreateBudgetItemReallocationRequest> validator,
            RecordReallocationHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (await validator.ValidateEndpointRequest(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.HandleCanonical(
                budgetId,
                request.Date,
                request.Notes,
                request.Adjustments,
                ct);
            return result.ToHttpResult(httpContext, reallocation => Results.Created(
                $"/api/budgets/{budgetId}/reallocations/{reallocation.Id}",
                new BudgetReallocationDto(
                    reallocation.Id,
                    budgetId,
                    reallocation.Date,
                    reallocation.Notes,
                    request.Adjustments,
                    reallocation.CreatedAt)));
        });
    }
}
