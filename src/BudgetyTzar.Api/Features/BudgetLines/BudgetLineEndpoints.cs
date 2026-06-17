using BudgetyTzar.Api.Data;
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

            var name = request.Name.Trim();
            if (await db.BudgetLines.AnyAsync(x => x.BudgetId == budgetId && x.Name == name, ct))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Name)] = ["A budget line with this name already exists in this budget."]
                });
            }

            var line = new BudgetLine
            {
                BudgetId = budgetId,
                Name = name,
                Direction = request.Direction,
                RolloverType = request.RolloverType
            };
            db.BudgetLines.Add(line);
            AddAudit(db, budgetId, null, nameof(BudgetLine), line.Id, "BudgetLineCreated", $"Created budget line {line.Name}.");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budgets/{budgetId}/budget-lines/{line.Id}", line);
        });

        budgets.MapPost("/{budgetId:guid}/budget-lines/{lineId:guid}/archive", async (
            Guid budgetId,
            Guid lineId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var line = await db.BudgetLines.FirstOrDefaultAsync(x => x.Id == lineId && x.BudgetId == budgetId, ct);
            if (line is null)
            {
                return Results.NotFound();
            }

            line.IsArchived = true;
            AddAudit(db, budgetId, null, nameof(BudgetLine), line.Id, "BudgetLineArchived", $"Archived budget line {line.Name}.");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
