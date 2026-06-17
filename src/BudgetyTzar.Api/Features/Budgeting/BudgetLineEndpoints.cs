using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetLineRequest(string Name, BudgetLineDirection Direction, BudgetLineRolloverType RolloverType);
public sealed class CreateBudgetLineValidator : AbstractValidator<CreateBudgetLineRequest>
{
    public CreateBudgetLineValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.RolloverType).IsInEnum();
    }
}

public static partial class Endpoints
{
    private static void MapBudgetLineEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/budget-lines", async (Guid budgetId, BudgetDbContext db, CancellationToken ct) =>
        {
            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var lines = await db.BudgetLines
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId)
                .OrderBy(x => x.Direction)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);
            return Results.Ok(lines);
        });

        budgets.MapPost("/{budgetId:guid}/budget-lines", async (
            Guid budgetId,
            CreateBudgetLineRequest request,
            IValidator<CreateBudgetLineRequest> validator,
            CreateBudgetLineHandler handler,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(budgetId, request.Name, request.Direction, request.RolloverType, ct);
            return result.ToHttpResult(line => Results.Created($"/api/budgets/{budgetId}/budget-lines/{line.Id}", line));
        });

        budgets.MapPost("/{budgetId:guid}/budget-lines/{lineId:guid}/archive", async (
            Guid budgetId,
            Guid lineId,
            ArchiveBudgetLineHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(budgetId, lineId, ct);
            return result.ToHttpResult();
        });
    }
}
