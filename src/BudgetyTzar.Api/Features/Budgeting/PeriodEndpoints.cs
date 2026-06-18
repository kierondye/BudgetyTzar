using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Infrastructure.Persistence;
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
        }).ExcludeFromDescription();

        budgets.MapGet("/{budgetId:guid}/periods/for-date", async (Guid budgetId, DateOnly date, BudgetDbContext db, CancellationToken ct) =>
            await db.BudgetPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.StartDate <= date && x.EndDate >= date, ct) is { } period
                ? Results.Ok(period)
                : Results.NotFound()).ExcludeFromDescription();

        budgets.MapGet("/{budgetId:guid}/periods/{periodId:guid}", async (Guid budgetId, Guid periodId, BudgetDbContext db, CancellationToken ct) =>
            await db.BudgetPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == periodId && x.BudgetId == budgetId, ct) is { } period
                ? Results.Ok(period)
                : Results.NotFound()).ExcludeFromDescription();

        budgets.MapPost("/{budgetId:guid}/periods", async (
            Guid budgetId,
            CreateBudgetPeriodRequest request,
            IValidator<CreateBudgetPeriodRequest> validator,
            CreateBudgetPeriodHandler handler,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(
                budgetId,
                request.Name,
                request.StartDate,
                request.EndDate,
                request.Allocations,
                request.CopyAllocationsFromPeriodId,
                ct);
            return result.ToHttpResult(period => Results.Created($"/api/budgets/{budgetId}/periods/{period.Id}", period));
        }).ExcludeFromDescription();
    }
}
