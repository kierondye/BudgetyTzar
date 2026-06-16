using BudgetyTzar.Api.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetCategoryRequest(string Name, BudgetCategoryType Type);
public sealed record CreateIncomeSourceRequest(string Name, bool IsRecurring);
public sealed record CreateBudgetPeriodRequest(string Name, DateOnly StartDate, DateOnly EndDate);
public sealed record PlanCategoryAllocationRequest(Guid BudgetCategoryId, decimal Amount, string Currency);
public sealed record PlanIncomeRequest(Guid IncomeSourceId, decimal Amount, string Currency);
public sealed record CreateTransactionRequest(
    Guid BudgetPeriodId,
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    string Currency,
    TransactionDirection Direction,
    string? SourceAccount,
    string? ExternalReference,
    string? Notes);
public sealed record AssignTransactionRequest(TransactionAssignmentTargetType TargetType, Guid TargetId, decimal Amount, string Currency);
public sealed record MoveBudgetRequest(Guid FromCategoryId, Guid ToCategoryId, decimal Amount, string Currency, string Reason);
public sealed record TransactionDetail(FinancialTransaction Transaction, IReadOnlyList<TransactionAssignment> Assignments);
public sealed record AuditTimelineItem(DateTimeOffset OccurredAt, string EventType, Guid EntityId, string Description);

public sealed class CreateBudgetCategoryValidator : AbstractValidator<CreateBudgetCategoryRequest>
{
    public CreateBudgetCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).IsInEnum();
    }
}

public sealed class CreateIncomeSourceValidator : AbstractValidator<CreateIncomeSourceRequest>
{
    public CreateIncomeSourceValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
}

public sealed class CreateBudgetPeriodValidator : AbstractValidator<CreateBudgetPeriodRequest>
{
    public CreateBudgetPeriodValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate);
    }
}

public sealed class PlanCategoryAllocationValidator : AbstractValidator<PlanCategoryAllocationRequest>
{
    public PlanCategoryAllocationValidator()
    {
        RuleFor(x => x.BudgetCategoryId).NotEmpty();
        RuleFor(x => x.Amount).PositiveAmount();
        RuleFor(x => x.Currency).Currency();
    }
}

public sealed class PlanIncomeValidator : AbstractValidator<PlanIncomeRequest>
{
    public PlanIncomeValidator()
    {
        RuleFor(x => x.IncomeSourceId).NotEmpty();
        RuleFor(x => x.Amount).PositiveAmount();
        RuleFor(x => x.Currency).Currency();
    }
}

public sealed class CreateTransactionValidator : AbstractValidator<CreateTransactionRequest>
{
    public CreateTransactionValidator()
    {
        RuleFor(x => x.BudgetPeriodId).NotEmpty();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(240);
        RuleFor(x => x.Amount).PositiveAmount();
        RuleFor(x => x.Currency).Currency();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.SourceAccount).MaximumLength(120);
        RuleFor(x => x.ExternalReference).MaximumLength(160);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class AssignTransactionValidator : AbstractValidator<AssignTransactionRequest>
{
    public AssignTransactionValidator()
    {
        RuleFor(x => x.TargetType).IsInEnum();
        RuleFor(x => x.TargetId).NotEmpty();
        RuleFor(x => x.Amount).PositiveAmount();
        RuleFor(x => x.Currency).Currency();
    }
}

public sealed class MoveBudgetValidator : AbstractValidator<MoveBudgetRequest>
{
    public MoveBudgetValidator()
    {
        RuleFor(x => x.FromCategoryId).NotEmpty();
        RuleFor(x => x.ToCategoryId).NotEmpty().NotEqual(x => x.FromCategoryId);
        RuleFor(x => x.Amount).PositiveAmount();
        RuleFor(x => x.Currency).Currency();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public static class Endpoints
{
    public static RouteGroupBuilder MapBudgetCategoryEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/budget-categories").WithTags("Budget categories");

        group.MapGet("/", async (BudgetDbContext db, CancellationToken ct) =>
            await db.BudgetCategories.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct));

        group.MapPost("/", async (
            CreateBudgetCategoryRequest request,
            IValidator<CreateBudgetCategoryRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var category = new BudgetCategory { Name = request.Name.Trim(), Type = request.Type };
            db.BudgetCategories.Add(category);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budget-categories/{category.Id}", category);
        });

        group.MapPost("/{id:guid}/archive", async (Guid id, BudgetDbContext db, CancellationToken ct) =>
        {
            var category = await db.BudgetCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (category is null)
            {
                return Results.NotFound();
            }

            category.IsArchived = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return group;
    }

    public static RouteGroupBuilder MapIncomeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/income-sources").WithTags("Income sources");

        group.MapGet("/", async (BudgetDbContext db, CancellationToken ct) =>
            await db.IncomeSources.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct));

        group.MapPost("/", async (
            CreateIncomeSourceRequest request,
            IValidator<CreateIncomeSourceRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var incomeSource = new IncomeSource { Name = request.Name.Trim(), IsRecurring = request.IsRecurring };
            db.IncomeSources.Add(incomeSource);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/income-sources/{incomeSource.Id}", incomeSource);
        });

        return group;
    }

    public static RouteGroupBuilder MapBudgetPeriodEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/budget-periods").WithTags("Budget periods");

        group.MapGet("/", async (BudgetDbContext db, CancellationToken ct) =>
            await db.BudgetPeriods.AsNoTracking().OrderByDescending(x => x.StartDate).ToListAsync(ct));

        group.MapGet("/{id:guid}", async (Guid id, BudgetDbContext db, CancellationToken ct) =>
            await db.BudgetPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) is { } period
                ? Results.Ok(period)
                : Results.NotFound());

        group.MapPost("/", async (
            CreateBudgetPeriodRequest request,
            IValidator<CreateBudgetPeriodRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var period = new BudgetPeriod
            {
                Name = request.Name.Trim(),
                StartDate = request.StartDate,
                EndDate = request.EndDate
            };
            db.BudgetPeriods.Add(period);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budget-periods/{period.Id}", period);
        });

        group.MapPost("/{id:guid}/allocations", async (
            Guid id,
            PlanCategoryAllocationRequest request,
            IValidator<PlanCategoryAllocationRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            if (!await db.BudgetPeriods.AnyAsync(x => x.Id == id, ct)
                || !await db.BudgetCategories.AnyAsync(x => x.Id == request.BudgetCategoryId && !x.IsArchived, ct))
            {
                return Results.NotFound();
            }

            var allocation = new CategoryAllocation
            {
                BudgetPeriodId = id,
                BudgetCategoryId = request.BudgetCategoryId,
                Amount = request.Amount,
                Currency = request.Currency
            };
            db.CategoryAllocations.Add(allocation);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budget-periods/{id}/allocations/{allocation.Id}", allocation);
        });

        group.MapPost("/{id:guid}/expected-income", async (
            Guid id,
            PlanIncomeRequest request,
            IValidator<PlanIncomeRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            if (!await db.BudgetPeriods.AnyAsync(x => x.Id == id, ct)
                || !await db.IncomeSources.AnyAsync(x => x.Id == request.IncomeSourceId && !x.IsArchived, ct))
            {
                return Results.NotFound();
            }

            var expectation = new IncomeExpectation
            {
                BudgetPeriodId = id,
                IncomeSourceId = request.IncomeSourceId,
                Amount = request.Amount,
                Currency = request.Currency
            };
            db.IncomeExpectations.Add(expectation);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budget-periods/{id}/expected-income/{expectation.Id}", expectation);
        });

        group.MapPost("/{id:guid}/movements", async (
            Guid id,
            MoveBudgetRequest request,
            IValidator<MoveBudgetRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            if (!await db.BudgetPeriods.AnyAsync(x => x.Id == id, ct)
                || !await db.BudgetCategories.AnyAsync(x => x.Id == request.FromCategoryId && !x.IsArchived, ct)
                || !await db.BudgetCategories.AnyAsync(x => x.Id == request.ToCategoryId && !x.IsArchived, ct))
            {
                return Results.NotFound();
            }

            var movement = new BudgetMovement
            {
                BudgetPeriodId = id,
                FromCategoryId = request.FromCategoryId,
                ToCategoryId = request.ToCategoryId,
                Amount = request.Amount,
                Currency = request.Currency,
                Reason = request.Reason.Trim()
            };
            db.BudgetMovements.Add(movement);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budget-periods/{id}/movements/{movement.Id}", movement);
        });

        return group;
    }

    public static RouteGroupBuilder MapTransactionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/transactions").WithTags("Transactions");

        group.MapGet("/", async (Guid? budgetPeriodId, BudgetDbContext db, CancellationToken ct) =>
        {
            var query = db.Transactions.AsNoTracking();
            if (budgetPeriodId.HasValue)
            {
                query = query.Where(x => x.BudgetPeriodId == budgetPeriodId.Value);
            }

            return await query.OrderByDescending(x => x.TransactionDate).ToListAsync(ct);
        });

        group.MapGet("/{id:guid}", async (Guid id, BudgetDbContext db, CancellationToken ct) =>
        {
            var transaction = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (transaction is null)
            {
                return Results.NotFound();
            }

            var assignments = await db.TransactionAssignments
                .AsNoTracking()
                .Where(x => x.TransactionId == id)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);
            return Results.Ok(new TransactionDetail(transaction, assignments));
        });

        group.MapPost("/", async (
            CreateTransactionRequest request,
            IValidator<CreateTransactionRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            if (!await db.BudgetPeriods.AnyAsync(x => x.Id == request.BudgetPeriodId, ct))
            {
                return Results.NotFound();
            }

            var transaction = new FinancialTransaction
            {
                BudgetPeriodId = request.BudgetPeriodId,
                TransactionDate = request.TransactionDate,
                Description = request.Description.Trim(),
                Amount = request.Amount,
                Currency = request.Currency,
                Direction = request.Direction,
                SourceAccount = request.SourceAccount,
                ExternalReference = request.ExternalReference,
                Notes = request.Notes
            };
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/transactions/{transaction.Id}", transaction);
        });

        group.MapPost("/{id:guid}/assign", async (
            Guid id,
            AssignTransactionRequest request,
            IValidator<AssignTransactionRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (transaction is null)
            {
                return Results.NotFound();
            }

            var targetExists = request.TargetType switch
            {
                TransactionAssignmentTargetType.BudgetCategory =>
                    await db.BudgetCategories.AnyAsync(x => x.Id == request.TargetId && !x.IsArchived, ct),
                TransactionAssignmentTargetType.IncomeSource =>
                    await db.IncomeSources.AnyAsync(x => x.Id == request.TargetId && !x.IsArchived, ct),
                _ => false
            };
            if (!targetExists)
            {
                return Results.NotFound();
            }

            if (request.Amount > transaction.Amount)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Amount)] = ["Assignment amount cannot exceed the transaction amount."]
                });
            }

            var assignment = new TransactionAssignment
            {
                TransactionId = id,
                TargetType = request.TargetType,
                TargetId = request.TargetId,
                Amount = request.Amount,
                Currency = request.Currency
            };
            db.TransactionAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/transactions/{id}/assignments/{assignment.Id}", assignment);
        });

        group.MapPost("/{id:guid}/ignore", async (Guid id, BudgetDbContext db, CancellationToken ct) =>
        {
            var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (transaction is null)
            {
                return Results.NotFound();
            }

            transaction.IsIgnored = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return group;
    }

    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/reports/monthly-summary", async (Guid budgetPeriodId, BudgetDbContext db, CancellationToken ct) =>
            await DashboardQueries.GetMonthlyDashboard(db, budgetPeriodId, ct) is { } dashboard
                ? Results.Ok(dashboard)
                : Results.NotFound())
            .WithTags("Reports");

        api.MapGet("/reports/audit-timeline", async (Guid budgetPeriodId, BudgetDbContext db, CancellationToken ct) =>
        {
            var transactionIds = await db.Transactions
                .AsNoTracking()
                .Where(x => x.BudgetPeriodId == budgetPeriodId)
                .Select(x => x.Id)
                .ToListAsync(ct);

            var assignmentItems = await db.TransactionAssignments
                .AsNoTracking()
                .Where(x => transactionIds.Contains(x.TransactionId))
                .Select(x => new AuditTimelineItem(
                    x.CreatedAt,
                    "TransactionAssigned",
                    x.Id,
                    $"Assigned {x.Amount} {x.Currency} to {x.TargetType} {x.TargetId}"))
                .ToListAsync(ct);

            var movementItems = await db.BudgetMovements
                .AsNoTracking()
                .Where(x => x.BudgetPeriodId == budgetPeriodId)
                .Select(x => new AuditTimelineItem(
                    x.CreatedAt,
                    "BudgetMovementRecorded",
                    x.Id,
                    $"Moved {x.Amount} {x.Currency} from category {x.FromCategoryId} to category {x.ToCategoryId}: {x.Reason}"))
                .ToListAsync(ct);

            return assignmentItems
                .Concat(movementItems)
                .OrderByDescending(x => x.OccurredAt)
                .ToList();
        })
        .WithTags("Reports");

        return api;
    }
}
