using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapBudgetItemEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/budget-items", async (Guid budgetId, BudgetDbContext db, CancellationToken ct) =>
        {
            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var items = await db.BudgetItems
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId)
                .OrderBy(x => x.Name)
                .Select(x => new BudgetItemDto(x.Id, x.BudgetId, x.Name, x.IsArchived, x.ArchivedAt, x.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        budgets.MapPost("/{budgetId:guid}/budget-items", async (
            Guid budgetId,
            CreateBudgetItemRequest request,
            IValidator<CreateBudgetItemRequest> validator,
            CreateBudgetItemHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
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
