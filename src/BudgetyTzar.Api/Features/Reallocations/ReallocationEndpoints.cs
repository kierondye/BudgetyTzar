using BudgetyTzar.Api.Data;
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

            var lineIds = new[] { request.FromBudgetLineId, request.ToBudgetLineId };
            var budgetLines = await GetEligibleBudgetLines(db, budgetId, periodId, lineIds, ct);
            if (budgetLines.Count != lineIds.Length)
            {
                return Results.NotFound();
            }

            var nonDebitLineIds = budgetLines
                .Where(x => x.Direction != BudgetLineDirection.Debit)
                .Select(x => x.Id)
                .ToArray();
            if (nonDebitLineIds.Length > 0)
            {
                var errors = new Dictionary<string, string[]>
                {
                    [nameof(request)] = ["Budget reallocations can only be created between debit budget lines."]
                };
                if (nonDebitLineIds.Contains(request.FromBudgetLineId))
                {
                    errors[nameof(request.FromBudgetLineId)] = ["Source budget line must be a debit line."];
                }

                if (nonDebitLineIds.Contains(request.ToBudgetLineId))
                {
                    errors[nameof(request.ToBudgetLineId)] = ["Target budget line must be a debit line."];
                }

                return Results.ValidationProblem(errors);
            }

            var summary = await DashboardQueries.GetPeriodSummary(db, budgetId, periodId, ct);
            var sourceLine = summary?.Lines.FirstOrDefault(x => x.BudgetLineId == request.FromBudgetLineId);
            if (sourceLine is null)
            {
                return Results.NotFound();
            }

            if (sourceLine.ClosingBalance < request.Amount)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Amount)] = ["Reallocation amount cannot exceed the source budget line's available balance."]
                });
            }

            var reallocation = new BudgetReallocation
            {
                BudgetPeriodId = periodId,
                FromBudgetLineId = request.FromBudgetLineId,
                ToBudgetLineId = request.ToBudgetLineId,
                Amount = request.Amount,
                Reason = request.Reason.Trim()
            };
            db.BudgetReallocations.Add(reallocation);
            AddAudit(db, budgetId, periodId, nameof(BudgetReallocation), reallocation.Id, "BudgetReallocationRecorded", $"Reallocated {reallocation.Amount} from budget line {reallocation.FromBudgetLineId} to budget line {reallocation.ToBudgetLineId}: {reallocation.Reason}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budgets/{budgetId}/periods/{periodId}/reallocations/{reallocation.Id}", reallocation);
        });
    }
}
