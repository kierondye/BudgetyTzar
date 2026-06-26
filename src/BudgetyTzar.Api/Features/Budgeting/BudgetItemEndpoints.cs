using FluentValidation;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapBudgetItemEndpoints(RouteGroupBuilder budgets)
    {
        MapListBudgetItemsEndpoint(budgets);
        budgets.MapPost("/{budgetId:guid}/budget-items", async (
            Guid budgetId,
            CreateBudgetItemRequest request,
            IValidator<CreateBudgetItemRequest> validator,
            CreateBudgetItemHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (await validator.ValidateEndpointRequest(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(budgetId, request.Name, ct);
            return result.ToHttpResult(httpContext, item => Results.Created(
                $"/api/budgets/{budgetId}/budget-items/{item.Id}",
                new BudgetItemDto(item.Id, item.BudgetId, item.Name, item.IsArchived, item.ArchivedAt, item.CreatedAt)));
        });

        budgets.MapPost("/{budgetId:guid}/budget-items/{budgetItemId:guid}/archive", async (
            Guid budgetId,
            Guid budgetItemId,
            ArchiveBudgetItemHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(budgetId, budgetItemId, ct);
            return result.ToHttpResult(httpContext, budgetId);
        });
    }
}
