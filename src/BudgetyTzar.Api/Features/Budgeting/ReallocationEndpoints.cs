using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetReallocationRequest(Guid FromBudgetLineId, Guid ToBudgetLineId, decimal Amount, string Reason);
public sealed class CreateBudgetReallocationValidator : AbstractValidator<CreateBudgetReallocationRequest>
{
    public CreateBudgetReallocationValidator()
    {
        RuleFor(x => x.FromBudgetLineId).NotEmpty();
        RuleFor(x => x.ToBudgetLineId).NotEmpty().NotEqual(x => x.FromBudgetLineId);
        RuleFor(x => x.Amount).PositiveAmount();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public static partial class Endpoints
{
    private static void MapReallocationEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/periods/{periodId:guid}/reallocations", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await PeriodBelongsToBudget(db, budgetId, periodId, ct))
            {
                return Results.NotFound();
            }

            var reallocations = await db.BudgetReallocations
                .AsNoTracking()
                .Where(x => x.BudgetPeriodId == periodId)
                .OrderByDescending(x => x.Id)
                .ToListAsync(ct);
            return Results.Ok(reallocations);
        });

        budgets.MapPost("/{budgetId:guid}/periods/{periodId:guid}/reallocations", async (
            Guid budgetId,
            Guid periodId,
            CreateBudgetReallocationRequest request,
            IValidator<CreateBudgetReallocationRequest> validator,
            RecordReallocationHandler handler,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(
                budgetId,
                periodId,
                request.FromBudgetLineId,
                request.ToBudgetLineId,
                request.Amount,
                request.Reason,
                ct);
            return result.ToHttpResult(reallocation => Results.Created($"/api/budgets/{budgetId}/periods/{periodId}/reallocations/{reallocation.Id}", reallocation));
        });
    }
}
