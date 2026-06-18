using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetItemRequest(string Name);
public sealed record BudgetItemDto(Guid Id, Guid BudgetId, string Name, bool IsArchived, DateTimeOffset CreatedAt);
public sealed class CreateBudgetItemValidator : AbstractValidator<CreateBudgetItemRequest>
{
    public CreateBudgetItemValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
    }
}

public static partial class Endpoints
{
    private static void MapBudgetLineEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/budget-items", async (Guid budgetId, BudgetDbContext db, CancellationToken ct) =>
        {
            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var items = await db.BudgetLines
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId)
                .OrderBy(x => x.Name)
                .Select(x => new BudgetItemDto(x.Id, x.BudgetId, x.Name, x.IsArchived, x.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        budgets.MapPost("/{budgetId:guid}/budget-items", async (
            Guid budgetId,
            CreateBudgetItemRequest request,
            IValidator<CreateBudgetItemRequest> validator,
            CreateBudgetLineHandler handler,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(budgetId, request.Name, BudgetLineDirection.Debit, BudgetLineRolloverType.Cumulative, ct);
            return result.ToHttpResult(line => Results.Created(
                $"/api/budgets/{budgetId}/budget-items/{line.Id}",
                new BudgetItemDto(line.Id, line.BudgetId, line.Name, line.IsArchived, line.CreatedAt)));
        });

        budgets.MapPost("/{budgetId:guid}/budget-items/{budgetItemId:guid}/archive", async (
            Guid budgetId,
            Guid budgetItemId,
            ArchiveBudgetLineHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(budgetId, budgetItemId, ct);
            return result.ToHttpResult();
        });
    }
}
