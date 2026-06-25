using FluentValidation;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapAdjustmentEndpoints(RouteGroupBuilder budgets)
    {
        MapListBudgetAdjustmentsEndpoint(budgets);
        budgets.MapPost("/{budgetId:guid}/budget-items/{budgetItemId:guid}/adjustments", async (
            Guid budgetId,
            Guid budgetItemId,
            CreateBudgetItemAdjustmentRequest request,
            IValidator<CreateBudgetItemAdjustmentRequest> validator,
            RecordAdjustmentHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.HandleCanonical(
                budgetId,
                budgetItemId,
                request.Amount,
                request.Direction,
                request.Date,
                request.Notes,
                ct);
            return result.ToHttpResult(httpContext, adjustment => Results.Created(
                $"/api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments/{adjustment.Id}",
                new BudgetAdjustmentDto(
                    adjustment.Id,
                    budgetId,
                    adjustment.BudgetItemId,
                    adjustment.ReallocationId,
                    adjustment.Date,
                    adjustment.Amount,
                    adjustment.Type,
                    adjustment.Notes,
                    adjustment.CreatedAt)));
        });
    }
}
