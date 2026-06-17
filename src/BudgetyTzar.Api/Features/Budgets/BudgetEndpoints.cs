using BudgetyTzar.Api.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetRequest(string Name, string Currency);
public sealed class CreateBudgetValidator : AbstractValidator<CreateBudgetRequest>
{
    public CreateBudgetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Currency).Currency();
    }
}

public static partial class Endpoints
{
    public static RouteGroupBuilder MapBudgetEndpoints(this RouteGroupBuilder api)
    {
        var budgets = api.MapGroup("/budgets").WithTags("Budgets");

        budgets.MapGet("/", async (BudgetDbContext db, CancellationToken ct) =>
            await db.Budgets.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct));

        budgets.MapGet("/{budgetId:guid}", async (Guid budgetId, BudgetDbContext db, CancellationToken ct) =>
            await db.Budgets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == budgetId, ct) is { } budget
                ? Results.Ok(budget)
                : Results.NotFound());

        budgets.MapPost("/", async (
            CreateBudgetRequest request,
            IValidator<CreateBudgetRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var budget = new Budget
            {
                Name = request.Name.Trim(),
                Currency = request.Currency
            };
            db.Budgets.Add(budget);
            AddAudit(db, budget.Id, null, nameof(Budget), budget.Id, "BudgetCreated", $"Created budget {budget.Name}.");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budgets/{budget.Id}", budget);
        });

        MapPeriodEndpoints(budgets);
        MapBudgetLineEndpoints(budgets);
        MapAllocationEndpoints(budgets);
        MapTransactionEndpoints(budgets);
        MapTransactionImportEndpoints(budgets);
        MapReallocationEndpoints(budgets);
        MapAdjustmentEndpoints(budgets);
        MapReportEndpoints(budgets);

        return api;
    }
}
