using BudgetyTzar.Api.Data;
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
        });

        budgets.MapPut("/{budgetId:guid}/periods/{periodId:guid}/allocations", async (
            Guid budgetId,
            Guid periodId,
            ReplaceBudgetLineAllocationsRequest request,
            IValidator<ReplaceBudgetLineAllocationsRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            if (!await PeriodBelongsToBudget(db, budgetId, periodId, ct))
            {
                return Results.NotFound();
            }

            if (await ValidateBudgetLineAllocations(db, budgetId, periodId, request.Allocations, nameof(request.Allocations), ct) is { } allocationProblem)
            {
                return allocationProblem;
            }

            var existing = await db.BudgetLineAllocations
                .Where(x => x.BudgetPeriodId == periodId)
                .ToListAsync(ct);
            db.BudgetLineAllocations.RemoveRange(existing);
            db.BudgetLineAllocations.AddRange(request.Allocations.Select(x => new BudgetLineAllocation
            {
                BudgetPeriodId = periodId,
                BudgetLineId = x.BudgetLineId,
                Amount = x.Amount
            }));
            AddAudit(
                db,
                budgetId,
                periodId,
                nameof(BudgetLineAllocation),
                periodId,
                "BudgetLineAllocationsReplaced",
                $"Replaced {request.Allocations.Count} allocation(s).",
                $"Previous={existing.Count}; New={request.Allocations.Count}");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<IResult?> ValidateBudgetLineAllocations(
        BudgetDbContext db,
        Guid budgetId,
        Guid? periodId,
        IReadOnlyList<BudgetLineAllocationItem> allocations,
        string requestPropertyName,
        CancellationToken ct)
    {
        var lineIds = allocations.Select(x => x.BudgetLineId).ToArray();
        if (lineIds.Distinct().Count() != lineIds.Length)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [requestPropertyName] = ["A budget line can only be allocated once per period."]
            });
        }

        var lines = await GetEligibleBudgetLines(db, budgetId, periodId, lineIds, ct);
        if (lines.Count != lineIds.Length)
        {
            return Results.NotFound();
        }

        return null;
    }
}
