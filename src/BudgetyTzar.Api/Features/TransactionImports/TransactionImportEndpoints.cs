using BudgetyTzar.Api.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record PreviewTransactionImportRequest(string FileName, string CsvContent);
public sealed record TransactionImportDetail(TransactionImportBatch Batch, IReadOnlyList<TransactionImportRow> Rows);
public sealed class PreviewTransactionImportValidator : AbstractValidator<PreviewTransactionImportRequest>
{
    public PreviewTransactionImportValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(240);
        RuleFor(x => x.CsvContent).NotEmpty();
    }
}

public static partial class Endpoints
{
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
            await AddImportBatchPeriodAudits(
                db,
                budgetId,
                batch.Id,
                batch.FileName,
                rows,
                "TransactionImportBatchPreviewed",
                "Previewed",
                ct);
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
            await AddImportBatchPeriodAudits(
                db,
                budgetId,
                batch.Id,
                batch.FileName,
                rows,
                "TransactionImportBatchCommitted",
                "Committed",
                ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new TransactionImportDetail(batch, rows));
        });
    }
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
}
