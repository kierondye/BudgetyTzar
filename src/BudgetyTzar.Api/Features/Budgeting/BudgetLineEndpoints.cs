using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetLineRequest(string Name, BudgetLineDirection Direction, BudgetLineRolloverType RolloverType);
public sealed record CreateBudgetItemRequest(string Name);
public sealed record BudgetItemDto(Guid Id, Guid BudgetId, string Name, bool IsArchived, DateTimeOffset CreatedAt);
public sealed class CreateBudgetLineValidator : AbstractValidator<CreateBudgetLineRequest>
{
    public CreateBudgetLineValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.RolloverType).IsInEnum();
    }
}
public sealed class CreateBudgetItemValidator : AbstractValidator<CreateBudgetItemRequest>
{
    public CreateBudgetItemValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
    }
}

public static partial class Endpoints
{
    private static void MapBudgetLineEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/budget-items", async (Guid budgetId, BudgetDbContext db, CancellationToken ct) =>
        {
            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var items = await db.BudgetLines
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId)
                .OrderBy(x => x.Name)
                .Select(x => new BudgetItemDto(x.Id, x.BudgetId, x.Name, x.IsArchived, x.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        budgets.MapPost("/{budgetId:guid}/budget-items", async (
            Guid budgetId,
            CreateBudgetItemRequest request,
            IValidator<CreateBudgetItemRequest> validator,
            CreateBudgetLineHandler handler,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(budgetId, request.Name, BudgetLineDirection.Debit, BudgetLineRolloverType.Cumulative, ct);
            return result.ToHttpResult(line => Results.Created(
                $"/api/budgets/{budgetId}/budget-items/{line.Id}",
                new BudgetItemDto(line.Id, line.BudgetId, line.Name, line.IsArchived, line.CreatedAt)));
        });

        budgets.MapPost("/{budgetId:guid}/budget-items/{budgetItemId:guid}/archive", async (
            Guid budgetId,
            Guid budgetItemId,
            ArchiveBudgetLineHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(budgetId, budgetItemId, ct);
            return result.ToHttpResult();
        });

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
        }).ExcludeFromDescription();

        budgets.MapPost("/{budgetId:guid}/budget-lines", async (
            Guid budgetId,
            CreateBudgetLineRequest request,
            IValidator<CreateBudgetLineRequest> validator,
            CreateBudgetLineHandler handler,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(budgetId, request.Name, request.Direction, request.RolloverType, ct);
            return result.ToHttpResult(line => Results.Created($"/api/budgets/{budgetId}/budget-lines/{line.Id}", line));
        }).ExcludeFromDescription();

        budgets.MapPost("/{budgetId:guid}/budget-lines/{lineId:guid}/archive", async (
            Guid budgetId,
            Guid lineId,
            ArchiveBudgetLineHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(budgetId, lineId, ct);
            return result.ToHttpResult();
        }).ExcludeFromDescription();
    }
}
