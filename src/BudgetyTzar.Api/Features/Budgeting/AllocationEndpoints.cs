using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record ReplaceBudgetLineAllocationsRequest(IReadOnlyList<BudgetLineAllocationItem> Allocations);
public sealed class ReplaceBudgetLineAllocationsValidator : AbstractValidator<ReplaceBudgetLineAllocationsRequest>
{
    public ReplaceBudgetLineAllocationsValidator()
    {
        RuleFor(x => x.Allocations).NotNull();
        RuleForEach(x => x.Allocations).ChildRules(item =>
        {
            item.RuleFor(x => x.BudgetLineId).NotEmpty();
            item.RuleFor(x => x.Amount).PositiveAmount();
        });
    }
}

public static partial class Endpoints
{
    private static void MapAllocationEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/periods/{periodId:guid}/allocations", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await PeriodBelongsToBudget(db, budgetId, periodId, ct))
            {
                return Results.NotFound();
            }

            var allocations = await db.BudgetLineAllocations
                .AsNoTracking()
                .Where(x => x.BudgetPeriodId == periodId)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);
            return Results.Ok(allocations);
        }).ExcludeFromDescription();

        budgets.MapPut("/{budgetId:guid}/periods/{periodId:guid}/allocations", async (
            Guid budgetId,
            Guid periodId,
            ReplaceBudgetLineAllocationsRequest request,
            IValidator<ReplaceBudgetLineAllocationsRequest> validator,
            ReplaceAllocationsHandler handler,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(budgetId, periodId, request.Allocations, ct);
            return result.ToHttpResult();
        }).ExcludeFromDescription();
    }
}
