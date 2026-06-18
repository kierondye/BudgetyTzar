using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetItemReallocationRequest(DateOnly Date, string? Notes, IReadOnlyList<BudgetReallocationAdjustmentItem> Adjustments);
public sealed record BudgetReallocationDto(
    Guid Id,
    Guid BudgetId,
    DateOnly Date,
    string? Notes,
    IReadOnlyList<BudgetReallocationAdjustmentItem> Adjustments,
    DateTimeOffset CreatedAt);
public sealed class CreateBudgetItemReallocationValidator : AbstractValidator<CreateBudgetItemReallocationRequest>
{
    public CreateBudgetItemReallocationValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Adjustments).NotNull().Must(x => x.Count >= 2)
            .WithMessage("A reallocation must contain at least two adjustments.");
        RuleForEach(x => x.Adjustments).ChildRules(item =>
        {
            item.RuleFor(x => x.BudgetItemId).NotEmpty();
            item.RuleFor(x => x.Amount).PositiveAmount();
            item.RuleFor(x => x.Direction).IsInEnum();
        });
    }
}

public static partial class Endpoints
{
    private static void MapReallocationEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/reallocations", async (
            Guid budgetId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var reallocations = await db.BudgetReallocations
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId)
                .ToListAsync(ct);
            reallocations = reallocations
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.CreatedAt)
                .ToList();
            var reallocationIds = reallocations.Select(x => x.Id).ToArray();
            var adjustments = await db.BudgetAdjustments
                .AsNoTracking()
                .Where(x => x.ReallocationId.HasValue && reallocationIds.Contains(x.ReallocationId.Value))
                .GroupBy(x => x.ReallocationId!.Value)
                .ToDictionaryAsync(
                    x => x.Key,
                    x => (IReadOnlyList<BudgetReallocationAdjustmentItem>)x
                        .Select(y => new BudgetReallocationAdjustmentItem(y.BudgetLineId, y.Amount, y.Type))
                        .ToList(),
                    ct);

            return Results.Ok(reallocations.Select(x => new BudgetReallocationDto(
                x.Id,
                x.BudgetId,
                x.Date,
                x.Notes,
                adjustments.GetValueOrDefault(x.Id, []),
                x.CreatedAt)).ToList());
        });

        budgets.MapPost("/{budgetId:guid}/reallocations", async (
            Guid budgetId,
            CreateBudgetItemReallocationRequest request,
            IValidator<CreateBudgetItemReallocationRequest> validator,
            RecordReallocationHandler handler,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.HandleCanonical(
                budgetId,
                request.Date,
                request.Notes,
                request.Adjustments,
                ct);
            return result.ToHttpResult(reallocation => Results.Created(
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
