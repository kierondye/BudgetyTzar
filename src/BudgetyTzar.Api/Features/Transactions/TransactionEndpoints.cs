using BudgetyTzar.Api.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateTransactionRequest(
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    TransactionDirection Direction,
    string? SourceAccount,
    string? ExternalReference,
    string? Notes);
public sealed record UpdateTransactionRequest(
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    TransactionDirection Direction,
    string? SourceAccount,
    string? ExternalReference,
    string? Notes);
public sealed record ReplaceTransactionAssignmentsRequest(IReadOnlyList<TransactionAssignmentItem> Assignments);
public sealed record TransactionDetail(FinancialTransaction Transaction, IReadOnlyList<TransactionAssignment> Assignments);
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

public sealed class UpdateTransactionValidator : AbstractValidator<UpdateTransactionRequest>
{
    public UpdateTransactionValidator()
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

public static partial class Endpoints
{
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
                .OrderBy(x => x.Id)
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
            var periodId = await FindPeriodIdForDate(db, budgetId, transaction.TransactionDate, ct);
            AddAudit(db, budgetId, periodId, nameof(FinancialTransaction), transaction.Id, "TransactionManuallyCreated", $"Created transaction {transaction.Description} for {transaction.Amount} {transaction.Direction}.");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budgets/{budgetId}/transactions/{transaction.Id}", transaction);
        });

        budgets.MapPut("/{budgetId:guid}/transactions/{transactionId:guid}", async (
            Guid budgetId,
            Guid transactionId,
            UpdateTransactionRequest request,
            IValidator<UpdateTransactionRequest> validator,
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

            var assignedTotal = await db.TransactionAssignments
                .AsNoTracking()
                .Where(x => x.TransactionId == transactionId)
                .SumAsync(x => x.Amount, ct);
            if (request.Amount < assignedTotal)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Amount)] = ["Transaction amount cannot be less than the current assigned total."]
                });
            }

            var previousPeriodId = await FindPeriodIdForDate(db, budgetId, transaction.TransactionDate, ct);
            var previousDescription = transaction.Description;
            var previousAmount = transaction.Amount;
            var previousDirection = transaction.Direction;

            transaction.TransactionDate = request.TransactionDate;
            transaction.Description = request.Description.Trim();
            transaction.Amount = request.Amount;
            transaction.Direction = request.Direction;
            transaction.SourceAccount = request.SourceAccount;
            transaction.ExternalReference = request.ExternalReference;
            transaction.Notes = request.Notes;

            var periodId = await FindPeriodIdForDate(db, budgetId, transaction.TransactionDate, ct);
            AddAudit(
                db,
                budgetId,
                periodId,
                nameof(FinancialTransaction),
                transaction.Id,
                "TransactionEdited",
                $"Edited transaction {transaction.Description}.",
                $"Previous={previousDescription}, {previousAmount} {previousDirection}, Period={previousPeriodId}; New={transaction.Description}, {transaction.Amount} {transaction.Direction}, Period={periodId}");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
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
            var periodId = await FindPeriodIdForDate(db, budgetId, transaction.TransactionDate, ct);
            AddAudit(db, budgetId, periodId, nameof(FinancialTransaction), transaction.Id, "TransactionIgnored", $"Ignored transaction {transaction.Description}.");
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
                .OrderBy(x => x.Id)
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

            var periodId = await FindPeriodIdForDate(db, budgetId, transaction.TransactionDate, ct);
            var budgetLines = await GetEligibleBudgetLines(db, budgetId, periodId, requestedLineIds, ct);
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
            AddAudit(
                db,
                budgetId,
                periodId,
                nameof(FinancialTransaction),
                transactionId,
                request.Assignments.Count > 1 ? "TransactionSplit" : "TransactionAssigned",
                $"Replaced assignments for transaction {transaction.Description}.",
                $"Previous={FormatAssignments(existing)}; New={FormatAssignments(request.Assignments)}");
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
            var transaction = await db.Transactions.AsNoTracking().FirstAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
            db.TransactionAssignments.RemoveRange(assignments);
            var periodId = await FindPeriodIdForDate(db, budgetId, transaction.TransactionDate, ct);
            AddAudit(
                db,
                budgetId,
                periodId,
                nameof(FinancialTransaction),
                transactionId,
                "TransactionAssignmentsCleared",
                $"Cleared assignments for transaction {transaction.Description}.",
                FormatAssignments(assignments));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
