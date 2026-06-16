using BudgetyTzar.Api.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using System.Text;

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
public sealed record CreateBudgetAdjustmentRequest(Guid BudgetLineId, decimal Amount, string Reason);
public sealed record PreviewTransactionImportRequest(string FileName, string CsvContent);
public sealed record TransactionImportDetail(TransactionImportBatch Batch, IReadOnlyList<TransactionImportRow> Rows);
public sealed record TransactionDetail(FinancialTransaction Transaction, IReadOnlyList<TransactionAssignment> Assignments);
public sealed record AuditTimelineItem(DateTimeOffset OccurredAt, string EventType, Guid EntityId, string Description);
public sealed record ReconciliationReport(
    Guid BudgetId,
    Guid BudgetPeriodId,
    decimal ImportedDebitTotal,
    decimal ImportedCreditTotal,
    decimal ManualDebitTotal,
    decimal ManualCreditTotal,
    decimal AssignedDebitTotal,
    decimal AssignedCreditTotal,
    decimal IgnoredDebitTotal,
    decimal IgnoredCreditTotal,
    decimal UnassignedDebitTotal,
    decimal UnassignedCreditTotal,
    decimal PartiallyAssignedDebitTotal,
    decimal PartiallyAssignedCreditTotal,
    decimal ReallocationTotal,
    decimal AdjustmentTotal,
    int DuplicateCandidateCount);
public sealed record BudgetLineTrendItem(Guid BudgetPeriodId, string PeriodName, DateOnly StartDate, DateOnly EndDate, decimal Allocated, decimal ActualAmount, decimal AdjustmentAmount, decimal ClosingBalance, bool IsOverBudget);
public sealed record CreditVarianceItem(Guid BudgetPeriodId, string PeriodName, DateOnly StartDate, DateOnly EndDate, decimal PlannedCredit, decimal ActualCredit, decimal CreditVariance);

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

public sealed class CreateBudgetAdjustmentValidator : AbstractValidator<CreateBudgetAdjustmentRequest>
{
    public CreateBudgetAdjustmentValidator()
    {
        RuleFor(x => x.BudgetLineId).NotEmpty();
        RuleFor(x => x.Amount).NotEqual(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class PreviewTransactionImportValidator : AbstractValidator<PreviewTransactionImportRequest>
{
    public PreviewTransactionImportValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(240);
        RuleFor(x => x.CsvContent).NotEmpty();
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
            AddAudit(db, budgetId, period.Id, nameof(BudgetPeriod), period.Id, "BudgetPeriodCreated", $"Created period {period.Name}.");
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
            var periodId = await FindPeriodIdForDate(db, budgetId, transaction.TransactionDate, ct);
            AddAudit(db, budgetId, periodId, nameof(FinancialTransaction), transaction.Id, "TransactionManuallyCreated", $"Created transaction {transaction.Description} for {transaction.Amount} {transaction.Direction}.");
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
            var periodId = await FindPeriodIdForDate(db, budgetId, transaction.TransactionDate, ct);
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

    private static void MapTransactionImportEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapPost("/{budgetId:guid}/transaction-imports/preview", async (
            Guid budgetId,
            PreviewTransactionImportRequest request,
            IValidator<PreviewTransactionImportRequest> validator,
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

            IReadOnlyList<ParsedImportRow> parsedRows;
            try
            {
                parsedRows = TransactionImportParsing.Parse(request.CsvContent);
            }
            catch (InvalidOperationException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.CsvContent)] = [ex.Message]
                });
            }

            var existingTransactions = await db.Transactions
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId)
                .ToListAsync(ct);

            var batch = new TransactionImportBatch
            {
                BudgetId = budgetId,
                FileName = request.FileName.Trim(),
                RowCount = parsedRows.Count
            };
            var rows = parsedRows.Select(row =>
            {
                var duplicateReason = FindDuplicateReason(row, existingTransactions);
                return new TransactionImportRow
                {
                    ImportBatchId = batch.Id,
                    RowNumber = row.RowNumber,
                    TransactionDate = row.TransactionDate,
                    Description = row.Description,
                    Amount = row.Amount,
                    Direction = row.Direction,
                    SourceAccount = row.SourceAccount,
                    ExternalReference = row.ExternalReference,
                    Notes = row.Notes,
                    IsDuplicateCandidate = duplicateReason is not null,
                    DuplicateReason = duplicateReason
                };
            }).ToList();

            batch.DuplicateCandidateCount = rows.Count(x => x.IsDuplicateCandidate);
            db.TransactionImportBatches.Add(batch);
            db.TransactionImportRows.AddRange(rows);
            AddAudit(db, budgetId, null, nameof(TransactionImportBatch), batch.Id, "TransactionImportBatchPreviewed", $"Previewed import batch {batch.FileName} with {batch.RowCount} row(s).");
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/budgets/{budgetId}/transaction-imports/{batch.Id}", new TransactionImportDetail(batch, rows));
        });

        budgets.MapGet("/{budgetId:guid}/transaction-imports/{batchId:guid}", async (
            Guid budgetId,
            Guid batchId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var batch = await db.TransactionImportBatches
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == batchId && x.BudgetId == budgetId, ct);
            if (batch is null)
            {
                return Results.NotFound();
            }

            var rows = await db.TransactionImportRows
                .AsNoTracking()
                .Where(x => x.ImportBatchId == batchId)
                .OrderBy(x => x.RowNumber)
                .ToListAsync(ct);
            return Results.Ok(new TransactionImportDetail(batch, rows));
        });

        budgets.MapPost("/{budgetId:guid}/transaction-imports/{batchId:guid}/commit", async (
            Guid budgetId,
            Guid batchId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var batch = await db.TransactionImportBatches.FirstOrDefaultAsync(x => x.Id == batchId && x.BudgetId == budgetId, ct);
            if (batch is null)
            {
                return Results.NotFound();
            }

            var rows = await db.TransactionImportRows
                .Where(x => x.ImportBatchId == batchId)
                .OrderBy(x => x.RowNumber)
                .ToListAsync(ct);

            if (batch.Status == TransactionImportBatchStatus.Committed)
            {
                return Results.Ok(new TransactionImportDetail(batch, rows));
            }

            foreach (var row in rows)
            {
                var transaction = new FinancialTransaction
                {
                    BudgetId = budgetId,
                    ImportBatchId = batch.Id,
                    TransactionDate = row.TransactionDate,
                    Description = row.Description,
                    Amount = row.Amount,
                    Direction = row.Direction,
                    SourceAccount = row.SourceAccount,
                    ExternalReference = row.ExternalReference,
                    Notes = row.Notes
                };
                db.Transactions.Add(transaction);
                row.IsCommitted = true;
                row.TransactionId = transaction.Id;

                var periodId = await FindPeriodIdForDate(db, budgetId, transaction.TransactionDate, ct);
                AddAudit(db, budgetId, periodId, nameof(FinancialTransaction), transaction.Id, "TransactionImported", $"Imported transaction {transaction.Description} from batch {batch.FileName}.");
            }

            batch.Status = TransactionImportBatchStatus.Committed;
            batch.CommittedAt = DateTimeOffset.UtcNow;
            AddAudit(db, budgetId, null, nameof(TransactionImportBatch), batch.Id, "TransactionImportBatchCommitted", $"Committed import batch {batch.FileName} with {rows.Count} row(s).");
            await db.SaveChangesAsync(ct);

            return Results.Ok(new TransactionImportDetail(batch, rows));
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
            AddAudit(db, budgetId, periodId, nameof(BudgetReallocation), reallocation.Id, "BudgetReallocationRecorded", $"Reallocated {reallocation.Amount} from budget line {reallocation.FromBudgetLineId} to budget line {reallocation.ToBudgetLineId}: {reallocation.Reason}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/budgets/{budgetId}/periods/{periodId}/reallocations/{reallocation.Id}", reallocation);
        });
    }

    private static void MapAdjustmentEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/periods/{periodId:guid}/adjustments", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await PeriodBelongsToBudget(db, budgetId, periodId, ct))
            {
                return Results.NotFound();
            }

            var adjustments = await db.BudgetAdjustments
                .AsNoTracking()
                .Where(x => x.BudgetPeriodId == periodId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
            return Results.Ok(adjustments);
        });

        budgets.MapPost("/{budgetId:guid}/periods/{periodId:guid}/adjustments", async (
            Guid budgetId,
            Guid periodId,
            CreateBudgetAdjustmentRequest request,
            IValidator<CreateBudgetAdjustmentRequest> validator,
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

            var line = await db.BudgetLines
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.BudgetLineId && x.BudgetId == budgetId && !x.IsArchived, ct);
            if (line is null)
            {
                return Results.NotFound();
            }

            var adjustment = new BudgetAdjustment
            {
                BudgetPeriodId = periodId,
                BudgetLineId = request.BudgetLineId,
                Amount = request.Amount,
                Reason = request.Reason.Trim()
            };
            db.BudgetAdjustments.Add(adjustment);
            AddAudit(db, budgetId, periodId, nameof(BudgetAdjustment), adjustment.Id, "BudgetAdjustmentRecorded", $"Recorded adjustment {adjustment.Amount} for budget line {line.Name}: {adjustment.Reason}");
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/budgets/{budgetId}/periods/{periodId}/adjustments/{adjustment.Id}", adjustment);
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

        budgets.MapGet("/{budgetId:guid}/reports/period-summary.csv", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
            await DashboardQueries.GetPeriodSummary(db, budgetId, periodId, ct) is { } summary
                ? Results.Text(ToPeriodSummaryCsv(summary), "text/csv", Encoding.UTF8)
                : Results.NotFound());

        budgets.MapGet("/{budgetId:guid}/reports/reconciliation", async (
            Guid budgetId,
            Guid periodId,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            var report = await GetReconciliationReport(db, budgetId, periodId, ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        budgets.MapGet("/{budgetId:guid}/reports/budget-line-trends", async (
            Guid budgetId,
            Guid budgetLineId,
            DateOnly from,
            DateOnly to,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (to < from)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(to)] = ["End date must be on or after start date."]
                });
            }

            if (!await db.BudgetLines.AnyAsync(x => x.Id == budgetLineId && x.BudgetId == budgetId, ct))
            {
                return Results.NotFound();
            }

            var periods = await db.BudgetPeriods
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId && x.StartDate <= to && x.EndDate >= from)
                .OrderBy(x => x.StartDate)
                .ToListAsync(ct);

            var trends = new List<BudgetLineTrendItem>();
            foreach (var period in periods)
            {
                var summary = await DashboardQueries.GetPeriodSummary(db, budgetId, period.Id, ct);
                var line = summary?.Lines.FirstOrDefault(x => x.BudgetLineId == budgetLineId);
                if (summary is not null && line is not null)
                {
                    trends.Add(new BudgetLineTrendItem(
                        period.Id,
                        period.Name,
                        period.StartDate,
                        period.EndDate,
                        line.Allocated,
                        line.ActualAmount,
                        line.AdjustmentAmount,
                        line.ClosingBalance,
                        line.IsOverBudget));
                }
            }

            return Results.Ok(trends);
        });

        budgets.MapGet("/{budgetId:guid}/reports/credit-variance", async (
            Guid budgetId,
            DateOnly from,
            DateOnly to,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (to < from)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(to)] = ["End date must be on or after start date."]
                });
            }

            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
            }

            var periods = await db.BudgetPeriods
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId && x.StartDate <= to && x.EndDate >= from)
                .OrderBy(x => x.StartDate)
                .ToListAsync(ct);

            var items = new List<CreditVarianceItem>();
            foreach (var period in periods)
            {
                var summary = await DashboardQueries.GetPeriodSummary(db, budgetId, period.Id, ct);
                if (summary is not null)
                {
                    items.Add(new CreditVarianceItem(
                        period.Id,
                        period.Name,
                        period.StartDate,
                        period.EndDate,
                        summary.PlannedCredit,
                        summary.ActualCredit,
                        summary.CreditVariance));
                }
            }

            return Results.Ok(items);
        });

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

            var items = await db.AuditEvents
                .AsNoTracking()
                .Where(x => x.BudgetId == budgetId && (x.BudgetPeriodId == periodId || x.BudgetPeriodId == null))
                .Select(x => new AuditTimelineItem(
                    x.OccurredAt,
                    x.EventType,
                    x.Id,
                    x.Description))
                .ToListAsync(ct);

            return Results.Ok(items.OrderByDescending(x => x.OccurredAt).ToList());
        });
    }

    private static Task<bool> BudgetExists(BudgetDbContext db, Guid budgetId, CancellationToken ct) =>
        db.Budgets.AnyAsync(x => x.Id == budgetId, ct);

    private static Task<bool> PeriodBelongsToBudget(BudgetDbContext db, Guid budgetId, Guid periodId, CancellationToken ct) =>
        db.BudgetPeriods.AnyAsync(x => x.Id == periodId && x.BudgetId == budgetId, ct);

    private static async Task<Guid?> FindPeriodIdForDate(BudgetDbContext db, Guid budgetId, DateOnly date, CancellationToken ct) =>
        (await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.StartDate <= date && x.EndDate >= date, ct))?.Id;

    private static void AddAudit(
        BudgetDbContext db,
        Guid budgetId,
        Guid? periodId,
        string entityType,
        Guid entityId,
        string eventType,
        string description,
        string? details = null) =>
        db.AuditEvents.Add(new AuditEvent
        {
            BudgetId = budgetId,
            BudgetPeriodId = periodId,
            EntityType = entityType,
            EntityId = entityId,
            EventType = eventType,
            Description = description,
            Details = details
        });

    private static string FormatAssignments(IEnumerable<TransactionAssignment> assignments) =>
        string.Join("; ", assignments.Select(x => $"{x.BudgetLineId}:{x.Amount}"));

    private static string FormatAssignments(IEnumerable<TransactionAssignmentItem> assignments) =>
        string.Join("; ", assignments.Select(x => $"{x.BudgetLineId}:{x.Amount}"));

    private static string? FindDuplicateReason(ParsedImportRow row, IReadOnlyCollection<FinancialTransaction> transactions)
    {
        if (!string.IsNullOrWhiteSpace(row.ExternalReference)
            && transactions.Any(x => string.Equals(x.ExternalReference, row.ExternalReference, StringComparison.OrdinalIgnoreCase)))
        {
            return "External reference already exists in this budget.";
        }

        var normalizedDescription = TransactionImportParsing.NormalizeForDuplicateMatch(row.Description);
        var normalizedSource = TransactionImportParsing.NormalizeForDuplicateMatch(row.SourceAccount);
        var fallbackMatch = transactions.Any(x =>
            x.TransactionDate == row.TransactionDate
            && x.Amount == row.Amount
            && x.Direction == row.Direction
            && TransactionImportParsing.NormalizeForDuplicateMatch(x.Description) == normalizedDescription
            && TransactionImportParsing.NormalizeForDuplicateMatch(x.SourceAccount) == normalizedSource);

        return fallbackMatch
            ? "Date, amount, direction, source account, and description match an existing transaction."
            : null;
    }

    private static async Task<ReconciliationReport?> GetReconciliationReport(
        BudgetDbContext db,
        Guid budgetId,
        Guid periodId,
        CancellationToken ct)
    {
        var period = await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == periodId && x.BudgetId == budgetId, ct);
        if (period is null)
        {
            return null;
        }

        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.TransactionDate >= period.StartDate && x.TransactionDate <= period.EndDate)
            .ToListAsync(ct);
        var transactionIds = transactions.Select(x => x.Id).ToArray();
        var assignmentTotals = await db.TransactionAssignments
            .AsNoTracking()
            .Where(x => transactionIds.Contains(x.TransactionId))
            .GroupBy(x => x.TransactionId)
            .Select(x => new { TransactionId = x.Key, Amount = x.Sum(y => y.Amount) })
            .ToDictionaryAsync(x => x.TransactionId, x => x.Amount, ct);
        var reallocations = await db.BudgetReallocations
            .AsNoTracking()
            .Where(x => x.BudgetPeriodId == periodId)
            .ToListAsync(ct);
        var adjustments = await db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => x.BudgetPeriodId == periodId)
            .ToListAsync(ct);
        var importBatchIds = await db.TransactionImportBatches
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var duplicateCandidates = await db.TransactionImportRows
            .AsNoTracking()
            .CountAsync(x => importBatchIds.Contains(x.ImportBatchId) && x.IsDuplicateCandidate && x.TransactionDate >= period.StartDate && x.TransactionDate <= period.EndDate, ct);

        var activeTransactions = transactions.Where(x => !x.IsIgnored).ToList();
        var assignedDebit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Debit)
            .Sum(x => Math.Min(x.Amount, assignmentTotals.GetValueOrDefault(x.Id)));
        var assignedCredit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Credit)
            .Sum(x => Math.Min(x.Amount, assignmentTotals.GetValueOrDefault(x.Id)));
        var unassignedDebit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Debit && assignmentTotals.GetValueOrDefault(x.Id) == 0)
            .Sum(x => x.Amount);
        var unassignedCredit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Credit && assignmentTotals.GetValueOrDefault(x.Id) == 0)
            .Sum(x => x.Amount);
        var partiallyAssignedDebit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Debit
                && assignmentTotals.GetValueOrDefault(x.Id) > 0
                && assignmentTotals.GetValueOrDefault(x.Id) < x.Amount)
            .Sum(x => x.Amount - assignmentTotals.GetValueOrDefault(x.Id));
        var partiallyAssignedCredit = activeTransactions
            .Where(x => x.Direction == TransactionDirection.Credit
                && assignmentTotals.GetValueOrDefault(x.Id) > 0
                && assignmentTotals.GetValueOrDefault(x.Id) < x.Amount)
            .Sum(x => x.Amount - assignmentTotals.GetValueOrDefault(x.Id));

        return new ReconciliationReport(
            budgetId,
            periodId,
            transactions.Where(x => x.ImportBatchId is not null && x.Direction == TransactionDirection.Debit).Sum(x => x.Amount),
            transactions.Where(x => x.ImportBatchId is not null && x.Direction == TransactionDirection.Credit).Sum(x => x.Amount),
            transactions.Where(x => x.ImportBatchId is null && x.Direction == TransactionDirection.Debit).Sum(x => x.Amount),
            transactions.Where(x => x.ImportBatchId is null && x.Direction == TransactionDirection.Credit).Sum(x => x.Amount),
            assignedDebit,
            assignedCredit,
            transactions.Where(x => x.IsIgnored && x.Direction == TransactionDirection.Debit).Sum(x => x.Amount),
            transactions.Where(x => x.IsIgnored && x.Direction == TransactionDirection.Credit).Sum(x => x.Amount),
            unassignedDebit,
            unassignedCredit,
            partiallyAssignedDebit,
            partiallyAssignedCredit,
            reallocations.Sum(x => x.Amount),
            adjustments.Sum(x => Math.Abs(x.Amount)),
            duplicateCandidates);
    }

    private static string ToPeriodSummaryCsv(PeriodSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("budgetLineId,name,direction,rolloverType,isArchived,openingBalance,allocated,reallocationIn,reallocationOut,actualAmount,adjustmentAmount,closingBalance,isOverBudget");
        foreach (var line in summary.Lines)
        {
            builder
                .Append(line.BudgetLineId).Append(',')
                .Append(EscapeCsv(line.Name)).Append(',')
                .Append(line.Direction).Append(',')
                .Append(line.RolloverType).Append(',')
                .Append(line.IsArchived).Append(',')
                .Append(line.OpeningBalance).Append(',')
                .Append(line.Allocated).Append(',')
                .Append(line.ReallocationIn).Append(',')
                .Append(line.ReallocationOut).Append(',')
                .Append(line.ActualAmount).Append(',')
                .Append(line.AdjustmentAmount).Append(',')
                .Append(line.ClosingBalance).Append(',')
                .Append(line.IsOverBudget)
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

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
