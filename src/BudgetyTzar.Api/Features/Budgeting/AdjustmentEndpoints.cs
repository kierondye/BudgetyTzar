using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetItemAdjustmentRequest(decimal Amount, BudgetAdjustmentType Direction, DateOnly Date, string? Notes);
public sealed record BudgetAdjustmentDto(
    Guid Id,
    Guid BudgetId,
    Guid BudgetItemId,
    Guid? ReallocationId,
    DateOnly Date,
    decimal Amount,
    BudgetAdjustmentType Direction,
    string? Notes,
    DateTimeOffset CreatedAt);
public sealed class CreateBudgetItemAdjustmentValidator : AbstractValidator<CreateBudgetItemAdjustmentRequest>
{
    public CreateBudgetItemAdjustmentValidator()
    {
        RuleFor(x => x.Amount).PositiveAmount();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public static partial class Endpoints
{
    private static void MapAdjustmentEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/budget-items/{budgetItemId:guid}/adjustments", async (
            Guid budgetId,
            Guid budgetItemId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.BudgetLines.AnyAsync(x => x.BudgetId == budgetId && x.Id == budgetItemId, ct))
            {
                return Results.NotFound();
            }

            var adjustments = await db.BudgetAdjustments
                .AsNoTracking()
                .Where(x => x.BudgetLineId == budgetItemId && (x.BudgetId == budgetId || x.BudgetId == Guid.Empty))
                .ToListAsync(ct);
            var dtos = adjustments
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.CreatedAt)
                .Select(x => new BudgetAdjustmentDto(
                    x.Id,
                    x.BudgetId == Guid.Empty ? budgetId : x.BudgetId,
                    x.BudgetLineId,
                    x.ReallocationId,
                    x.Date,
                    x.Amount,
                    x.Type,
                    x.Notes,
                    x.CreatedAt))
                .ToList();
            return Results.Ok(dtos);
        });

        budgets.MapPost("/{budgetId:guid}/budget-items/{budgetItemId:guid}/adjustments", async (
            Guid budgetId,
            Guid budgetItemId,
            CreateBudgetItemAdjustmentRequest request,
            IValidator<CreateBudgetItemAdjustmentRequest> validator,
            RecordAdjustmentHandler handler,
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
            return result.ToHttpResult(adjustment => Results.Created(
                $"/api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments/{adjustment.Id}",
                new BudgetAdjustmentDto(
                    adjustment.Id,
                    budgetId,
                    adjustment.BudgetLineId,
                    adjustment.ReallocationId,
                    adjustment.Date,
                    adjustment.Amount,
                    adjustment.Type,
                    adjustment.Notes,
                    adjustment.CreatedAt)));
        });
    }
}
