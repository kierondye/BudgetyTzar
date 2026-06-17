using BudgetyTzar.Api.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetPeriodRequest(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<BudgetLineAllocationItem>? Allocations = null,
    Guid? CopyAllocationsFromPeriodId = null);
public sealed class CreateBudgetPeriodValidator : AbstractValidator<CreateBudgetPeriodRequest>
{
    public CreateBudgetPeriodValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
        RuleFor(x => x.CopyAllocationsFromPeriodId)
            .NotEqual(Guid.Empty)
            .When(x => x.CopyAllocationsFromPeriodId.HasValue);
        RuleFor(x => x)
            .Must(x => x.Allocations is null || !x.CopyAllocationsFromPeriodId.HasValue)
            .WithName(nameof(CreateBudgetPeriodRequest.Allocations))
            .WithMessage("Specify either inline allocations or a source period to copy from, not both.");
        RuleForEach(x => x.Allocations).ChildRules(item =>
        {
            item.RuleFor(x => x.BudgetLineId).NotEmpty();
            item.RuleFor(x => x.Amount).PositiveAmount();
        });
    }
}

public static partial class Endpoints
{
    private static void MapPeriodEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/periods", async (Guid budgetId, BudgetDbContext db, CancellationToken ct) =>
        {
            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var periods = await db.BudgetPeriods
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId)
                .OrderByDescending(x => x.StartDate)
                .ToListAsync(ct);
            return Results.Ok(periods);
        });

        budgets.MapGet("/{budgetId:guid}/periods/for-date", async (Guid budgetId, DateOnly date, BudgetDbContext db, CancellationToken ct) =>
            await db.BudgetPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.StartDate <= date && x.EndDate >= date, ct) is { } period
                ? Results.Ok(period)
                : Results.NotFound());

        budgets.MapGet("/{budgetId:guid}/periods/{periodId:guid}", async (Guid budgetId, Guid periodId, BudgetDbContext db, CancellationToken ct) =>
            await db.BudgetPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == periodId && x.BudgetId == budgetId, ct) is { } period
                ? Results.Ok(period)
                : Results.NotFound());

        budgets.MapPost("/{budgetId:guid}/periods", async (
            Guid budgetId,
            CreateBudgetPeriodRequest request,
            IValidator<CreateBudgetPeriodRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var overlaps = await db.BudgetPeriods.AnyAsync(x =>
                x.BudgetId == budgetId
                && x.StartDate <= request.EndDate
                && request.StartDate <= x.EndDate,
                ct);
            if (overlaps)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.StartDate)] = ["Budget periods cannot overlap within the same budget."]
                });
            }

            IReadOnlyList<BudgetLineAllocationItem> allocations = [];
            if (request.Allocations is not null)
            {
                if (await ValidateBudgetLineAllocations(db, budgetId, null, request.Allocations, nameof(request.Allocations), ct) is { } allocationProblem)
                {
                    return allocationProblem;
                }

                allocations = request.Allocations;
            }
            else if (request.CopyAllocationsFromPeriodId.HasValue)
            {
                var sourcePeriodId = request.CopyAllocationsFromPeriodId.Value;
                if (!await PeriodBelongsToBudget(db, budgetId, sourcePeriodId, ct))
                {
                    return Results.NotFound();
                }

                allocations = await db.BudgetLineAllocations
                    .AsNoTracking()
                    .Join(
                        db.BudgetLines.AsNoTracking().Where(x => x.BudgetId == budgetId && !x.IsArchived),
                        allocation => allocation.BudgetLineId,
                        line => line.Id,
                        (allocation, _) => allocation)
                    .Where(x => x.BudgetPeriodId == sourcePeriodId)
                    .OrderBy(x => x.Id)
                    .Select(x => new BudgetLineAllocationItem(x.BudgetLineId, x.Amount))
                    .ToListAsync(ct);
            }

            var period = new BudgetPeriod
            {
                BudgetId = budgetId,
                Name = request.Name.Trim(),
                StartDate = request.StartDate,
                EndDate = request.EndDate
            };
            db.BudgetPeriods.Add(period);
            db.BudgetLineAllocations.AddRange(allocations.Select(x => new BudgetLineAllocation
            {
                BudgetPeriodId = period.Id,
                BudgetLineId = x.BudgetLineId,
                Amount = x.Amount
            }));
            AddAudit(db, budgetId, period.Id, nameof(BudgetPeriod), period.Id, "BudgetPeriodCreated", $"Created period {period.Name}.");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budgets/{budgetId}/periods/{period.Id}", period);
        });
    }
}
