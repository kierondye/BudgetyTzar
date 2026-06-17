using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetAdjustmentRequest(Guid BudgetLineId, decimal Amount, string Reason);
public sealed class CreateBudgetAdjustmentValidator : AbstractValidator<CreateBudgetAdjustmentRequest>
{
    public CreateBudgetAdjustmentValidator()
    {
        RuleFor(x => x.BudgetLineId).NotEmpty();
        RuleFor(x => x.Amount).NotEqual(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public static partial class Endpoints
{
    private static void MapAdjustmentEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/periods/{periodId:guid}/adjustments", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await PeriodBelongsToBudget(db, budgetId, periodId, ct))
            {
                return Results.NotFound();
            }

            var adjustments = await db.BudgetAdjustments
                .AsNoTracking()
                .Where(x => x.BudgetPeriodId == periodId)
                .OrderByDescending(x => x.Id)
                .ToListAsync(ct);
            return Results.Ok(adjustments);
        });

        budgets.MapPost("/{budgetId:guid}/periods/{periodId:guid}/adjustments", async (
            Guid budgetId,
            Guid periodId,
            CreateBudgetAdjustmentRequest request,
            IValidator<CreateBudgetAdjustmentRequest> validator,
            RecordAdjustmentHandler handler,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(budgetId, periodId, request.BudgetLineId, request.Amount, request.Reason, ct);
            return result.ToHttpResult(adjustment => Results.Created($"/api/budgets/{budgetId}/periods/{periodId}/adjustments/{adjustment.Id}", adjustment));
        });
    }
}
