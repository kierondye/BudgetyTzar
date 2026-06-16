using BudgetyTzar.Api.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetRequest(string Name, string Currency);
public sealed record CreateBudgetPeriodRequest(string Name, DateOnly StartDate, DateOnly EndDate);
public sealed record CreateBudgetLineRequest(string Name, BudgetLineDirection Direction, BudgetLineRolloverType RolloverType);
public sealed record BudgetLineAllocationItem(Guid BudgetLineId, decimal Amount);
public sealed record ReplaceBudgetLineAllocationsRequest(IReadOnlyList<BudgetLineAllocationItem> Allocations);
public sealed record CreateTransactionRequest(
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    TransactionDirection Direction,
    string? SourceAccount,
    string? ExternalReference,
    string? Notes);
public sealed record TransactionAssignmentItem(Guid BudgetLineId, decimal Amount);
public sealed record ReplaceTransactionAssignmentsRequest(IReadOnlyList<TransactionAssignmentItem> Assignments);
public sealed record CreateBudgetReallocationRequest(Guid FromBudgetLineId, Guid ToBudgetLineId, decimal Amount, string Reason);
public sealed record TransactionDetail(FinancialTransaction Transaction, IReadOnlyList<TransactionAssignment> Assignments);
public sealed record AuditTimelineItem(DateTimeOffset OccurredAt, string EventType, Guid EntityId, string Description);

public sealed class CreateBudgetValidator : AbstractValidator<CreateBudgetRequest>
{
    public CreateBudgetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Currency).Currency();
    }
}

public sealed class CreateBudgetPeriodValidator : AbstractValidator<CreateBudgetPeriodRequest>
{
    public CreateBudgetPeriodValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
    }
}

public sealed class CreateBudgetLineValidator : AbstractValidator<CreateBudgetLineRequest>
{
    public CreateBudgetLineValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.RolloverType).IsInEnum();
    }
}

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

public sealed class CreateTransactionValidator : AbstractValidator<CreateTransactionRequest>
{
    public CreateTransactionValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(240);
        RuleFor(x => x.Amount).PositiveAmount();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.SourceAccount).MaximumLength(120);
        RuleFor(x => x.ExternalReference).MaximumLength(160);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class ReplaceTransactionAssignmentsValidator : AbstractValidator<ReplaceTransactionAssignmentsRequest>
{
    public ReplaceTransactionAssignmentsValidator()
    {
        RuleFor(x => x.Assignments).NotNull();
        RuleForEach(x => x.Assignments).ChildRules(item =>
        {
            item.RuleFor(x => x.BudgetLineId).NotEmpty();
            item.RuleFor(x => x.Amount).PositiveAmount();
        });
    }
}

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

public static class Endpoints
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
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budgets/{budget.Id}", budget);
        });

        MapPeriodEndpoints(budgets);
        MapBudgetLineEndpoints(budgets);
        MapAllocationEndpoints(budgets);
        MapTransactionEndpoints(budgets);
        MapReallocationEndpoints(budgets);
        MapReportEndpoints(budgets);

        return api;
    }

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

            var period = new BudgetPeriod
            {
                BudgetId = budgetId,
                Name = request.Name.Trim(),
                StartDate = request.StartDate,
                EndDate = request.EndDate
            };
            db.BudgetPeriods.Add(period);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budgets/{budgetId}/periods/{period.Id}", period);
        });
    }

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

            var line = new BudgetLine
            {
                BudgetId = budgetId,
                Name = request.Name.Trim(),
                Direction = request.Direction,
                RolloverType = request.RolloverType
            };
            db.BudgetLines.Add(line);
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
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

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
                .OrderBy(x => x.CreatedAt)
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

            var lineIds = request.Allocations.Select(x => x.BudgetLineId).ToArray();
            if (lineIds.Distinct().Count() != lineIds.Length)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Allocations)] = ["A budget line can only be allocated once per period."]
                });
            }

            var validLineCount = await db.BudgetLines
                .CountAsync(x => lineIds.Contains(x.Id) && x.BudgetId == budgetId && !x.IsArchived, ct);
            if (validLineCount != lineIds.Length)
            {
                return Results.NotFound();
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
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static void MapTransactionEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/transactions", async (
            Guid budgetId,
            Guid? periodId,
            DateOnly? from,
            DateOnly? to,
            TransactionAssignmentStatus? assignmentStatus,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            if (periodId.HasValue)
            {
                var period = await db.BudgetPeriods
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == periodId.Value && x.BudgetId == budgetId, ct);
                if (period is null)
                {
                    return Results.NotFound();
                }

                from = period.StartDate;
                to = period.EndDate;
            }

            var query = db.Transactions.AsNoTracking().Where(x => x.BudgetId == budgetId);
            if (from.HasValue)
            {
                query = query.Where(x => x.TransactionDate >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(x => x.TransactionDate <= to.Value);
            }

            var transactions = await query.OrderByDescending(x => x.TransactionDate).ToListAsync(ct);
            if (!assignmentStatus.HasValue)
            {
                return Results.Ok(transactions);
            }

            var transactionIds = transactions.Select(x => x.Id).ToArray();
            var assignmentTotals = await db.TransactionAssignments
                .AsNoTracking()
                .Where(x => transactionIds.Contains(x.TransactionId))
                .GroupBy(x => x.TransactionId)
                .Select(x => new { TransactionId = x.Key, Amount = x.Sum(y => y.Amount) })
                .ToDictionaryAsync(x => x.TransactionId, x => x.Amount, ct);

            var filtered = transactions
                .Where(x => GetAssignmentStatus(x, assignmentTotals.GetValueOrDefault(x.Id)) == assignmentStatus.Value)
                .ToList();
            return Results.Ok(filtered);
        });

        budgets.MapGet("/{budgetId:guid}/transactions/{transactionId:guid}", async (
            Guid budgetId,
            Guid transactionId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var transaction = await db.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
            if (transaction is null)
            {
                return Results.NotFound();
            }

            var assignments = await db.TransactionAssignments
                .AsNoTracking()
                .Where(x => x.TransactionId == transactionId)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);
            return Results.Ok(new TransactionDetail(transaction, assignments));
        });

        budgets.MapPost("/{budgetId:guid}/transactions", async (
            Guid budgetId,
            CreateTransactionRequest request,
            IValidator<CreateTransactionRequest> validator,
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

            var transaction = new FinancialTransaction
            {
                BudgetId = budgetId,
                TransactionDate = request.TransactionDate,
                Description = request.Description.Trim(),
                Amount = request.Amount,
                Direction = request.Direction,
                SourceAccount = request.SourceAccount,
                ExternalReference = request.ExternalReference,
                Notes = request.Notes
            };
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budgets/{budgetId}/transactions/{transaction.Id}", transaction);
        });

        budgets.MapPost("/{budgetId:guid}/transactions/{transactionId:guid}/ignore", async (
            Guid budgetId,
            Guid transactionId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
            if (transaction is null)
            {
                return Results.NotFound();
            }

            transaction.IsIgnored = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        budgets.MapGet("/{budgetId:guid}/transactions/{transactionId:guid}/assignments", async (
            Guid budgetId,
            Guid transactionId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.Transactions.AnyAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct))
            {
                return Results.NotFound();
            }

            var assignments = await db.TransactionAssignments
                .AsNoTracking()
                .Where(x => x.TransactionId == transactionId)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);
            return Results.Ok(assignments);
        });

        budgets.MapPut("/{budgetId:guid}/transactions/{transactionId:guid}/assignments", async (
            Guid budgetId,
            Guid transactionId,
            ReplaceTransactionAssignmentsRequest request,
            IValidator<ReplaceTransactionAssignmentsRequest> validator,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
            if (transaction is null)
            {
                return Results.NotFound();
            }

            var requestedLineIds = request.Assignments.Select(x => x.BudgetLineId).ToArray();
            if (requestedLineIds.Distinct().Count() != requestedLineIds.Length)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Assignments)] = ["A budget line can only be assigned once per transaction."]
                });
            }

            var budgetLines = await db.BudgetLines
                .AsNoTracking()
                .Where(x => requestedLineIds.Contains(x.Id) && x.BudgetId == budgetId && !x.IsArchived)
                .ToListAsync(ct);
            if (budgetLines.Count != requestedLineIds.Length)
            {
                return Results.NotFound();
            }

            var totalAssigned = request.Assignments.Sum(x => x.Amount);
            if (totalAssigned > transaction.Amount)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Assignments)] = ["Total assigned amount cannot exceed the transaction amount."]
                });
            }

            var existing = await db.TransactionAssignments
                .Where(x => x.TransactionId == transactionId)
                .ToListAsync(ct);
            db.TransactionAssignments.RemoveRange(existing);
            db.TransactionAssignments.AddRange(request.Assignments.Select(x => new TransactionAssignment
            {
                TransactionId = transactionId,
                BudgetLineId = x.BudgetLineId,
                Amount = x.Amount
            }));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        budgets.MapDelete("/{budgetId:guid}/transactions/{transactionId:guid}/assignments", async (
            Guid budgetId,
            Guid transactionId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.Transactions.AnyAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct))
            {
                return Results.NotFound();
            }

            var assignments = await db.TransactionAssignments
                .Where(x => x.TransactionId == transactionId)
                .ToListAsync(ct);
            db.TransactionAssignments.RemoveRange(assignments);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

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
                .OrderByDescending(x => x.CreatedAt)
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
            var budgetLines = await db.BudgetLines
                .AsNoTracking()
                .Where(x => lineIds.Contains(x.Id) && x.BudgetId == budgetId && !x.IsArchived)
                .ToListAsync(ct);
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

            var reallocation = new BudgetReallocation
            {
                BudgetPeriodId = periodId,
                FromBudgetLineId = request.FromBudgetLineId,
                ToBudgetLineId = request.ToBudgetLineId,
                Amount = request.Amount,
                Reason = request.Reason.Trim()
            };
            db.BudgetReallocations.Add(reallocation);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budgets/{budgetId}/periods/{periodId}/reallocations/{reallocation.Id}", reallocation);
        });
    }

    private static void MapReportEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/reports/period-summary", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
            await DashboardQueries.GetPeriodSummary(db, budgetId, periodId, ct) is { } summary
                ? Results.Ok(summary)
                : Results.NotFound());

        budgets.MapGet("/{budgetId:guid}/reports/audit-timeline", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var period = await db.BudgetPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == periodId && x.BudgetId == budgetId, ct);
            if (period is null)
            {
                return Results.NotFound();
            }

            var transactionIds = await db.Transactions
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId
                    && x.TransactionDate >= period.StartDate
                    && x.TransactionDate <= period.EndDate)
                .Select(x => x.Id)
                .ToListAsync(ct);

            var assignmentItems = await db.TransactionAssignments
                .AsNoTracking()
                .Where(x => transactionIds.Contains(x.TransactionId))
                .Select(x => new AuditTimelineItem(
                    x.CreatedAt,
                    "TransactionAssigned",
                    x.Id,
                    $"Assigned {x.Amount} to budget line {x.BudgetLineId}"))
                .ToListAsync(ct);

            var reallocationItems = await db.BudgetReallocations
                .AsNoTracking()
                .Where(x => x.BudgetPeriodId == periodId)
                .Select(x => new AuditTimelineItem(
                    x.CreatedAt,
                    "BudgetReallocationRecorded",
                    x.Id,
                    $"Reallocated {x.Amount} from budget line {x.FromBudgetLineId} to budget line {x.ToBudgetLineId}: {x.Reason}"))
                .ToListAsync(ct);

            return Results.Ok(assignmentItems
                .Concat(reallocationItems)
                .OrderByDescending(x => x.OccurredAt)
                .ToList());
        });
    }

    private static Task<bool> BudgetExists(BudgetDbContext db, Guid budgetId, CancellationToken ct) =>
        db.Budgets.AnyAsync(x => x.Id == budgetId, ct);

    private static Task<bool> PeriodBelongsToBudget(BudgetDbContext db, Guid budgetId, Guid periodId, CancellationToken ct) =>
        db.BudgetPeriods.AnyAsync(x => x.Id == periodId && x.BudgetId == budgetId, ct);

    private static TransactionAssignmentStatus GetAssignmentStatus(FinancialTransaction transaction, decimal assignedAmount)
    {
        if (assignedAmount == 0)
        {
            return TransactionAssignmentStatus.Unassigned;
        }

        return assignedAmount < transaction.Amount
            ? TransactionAssignmentStatus.PartiallyAssigned
            : TransactionAssignmentStatus.FullyAssigned;
    }
}
