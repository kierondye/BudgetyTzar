using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Transactions;

public sealed record TransactionDetail(FinancialTransaction Transaction, IReadOnlyList<TransactionAssignment> Assignments);
public sealed record TransactionImportDetail(TransactionImportBatch Batch, IReadOnlyList<TransactionImportRow> Rows);

public sealed class CreateTransactionHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult<FinancialTransaction>> Handle(
        Guid budgetId,
        DateOnly transactionDate,
        string description,
        decimal amount,
        TransactionDirection direction,
        string? sourceAccount,
        string? externalReference,
        string? notes,
        CancellationToken ct)
    {
        if (!await db.Budgets.AnyAsync(x => x.Id == budgetId, ct))
        {
            return CommandResult<FinancialTransaction>.NotFound();
        }

        var transaction = FinancialTransaction.Create(budgetId, transactionDate, description, amount, direction, sourceAccount, externalReference, notes);
        db.Transactions.Add(transaction);
        var periodId = await FindPeriodIdForDate(budgetId, transaction.TransactionDate, ct);
        audit.Add(new DomainEvent(
            "TransactionManuallyCreated",
            budgetId,
            periodId,
            nameof(FinancialTransaction),
            transaction.Id,
            $"Created transaction {transaction.Description} for {transaction.Amount} {transaction.Direction}."));
        await db.SaveChangesAsync(ct);
        return CommandResult<FinancialTransaction>.Created(transaction);
    }

    private async Task<Guid?> FindPeriodIdForDate(Guid budgetId, DateOnly date, CancellationToken ct) =>
        (await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.StartDate <= date && x.EndDate >= date, ct))?.Id;
}

public sealed class UpdateTransactionHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult> Handle(
        Guid budgetId,
        Guid transactionId,
        DateOnly transactionDate,
        string description,
        decimal amount,
        TransactionDirection direction,
        string? sourceAccount,
        string? externalReference,
        string? notes,
        CancellationToken ct)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
        if (transaction is null)
        {
            return CommandResult.NotFound();
        }

        var assignedTotal = await db.TransactionAssignments
            .AsNoTracking()
            .Where(x => x.TransactionId == transactionId)
            .SumAsync(x => x.Amount, ct);
        if (amount < assignedTotal)
        {
            return CommandResult.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(amount)] = ["Transaction amount cannot be less than the current assigned total."]
            });
        }

        var previousPeriodId = await FindPeriodIdForDate(budgetId, transaction.TransactionDate, ct);
        var previousDescription = transaction.Description;
        var previousAmount = transaction.Amount;
        var previousDirection = transaction.Direction;

        transaction.Edit(transactionDate, description, amount, direction, sourceAccount, externalReference, notes);

        var periodId = await FindPeriodIdForDate(budgetId, transaction.TransactionDate, ct);
        audit.Add(new DomainEvent(
            "TransactionEdited",
            budgetId,
            periodId,
            nameof(FinancialTransaction),
            transaction.Id,
            $"Edited transaction {transaction.Description}.",
            $"Previous={previousDescription}, {previousAmount} {previousDirection}, Period={previousPeriodId}; New={transaction.Description}, {transaction.Amount} {transaction.Direction}, Period={periodId}"));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent();
    }

    private async Task<Guid?> FindPeriodIdForDate(Guid budgetId, DateOnly date, CancellationToken ct) =>
        (await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.StartDate <= date && x.EndDate >= date, ct))?.Id;
}

public sealed class IgnoreTransactionHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
        if (transaction is null)
        {
            return CommandResult.NotFound();
        }

        transaction.Ignore();
        var periodId = await FindPeriodIdForDate(budgetId, transaction.TransactionDate, ct);
        audit.Add(new DomainEvent(
            "TransactionIgnored",
            budgetId,
            periodId,
            nameof(FinancialTransaction),
            transaction.Id,
            $"Ignored transaction {transaction.Description}."));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent();
    }

    private async Task<Guid?> FindPeriodIdForDate(Guid budgetId, DateOnly date, CancellationToken ct) =>
        (await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.StartDate <= date && x.EndDate >= date, ct))?.Id;
}

public sealed class ReplaceTransactionAssignmentsHandler(BudgetDbContext db, AuditEventWriter audit, BudgetLineEligibilityService eligibility)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid transactionId, IReadOnlyList<TransactionAssignmentItem> assignments, CancellationToken ct)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
        if (transaction is null)
        {
            return CommandResult.NotFound();
        }

        var requestedLineIds = assignments.Select(x => x.BudgetLineId).ToArray();
        if (requestedLineIds.Distinct().Count() != requestedLineIds.Length)
        {
            return CommandResult.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(assignments)] = ["A budget line can only be assigned once per transaction."]
            });
        }

        var periodId = await FindPeriodIdForDate(budgetId, transaction.TransactionDate, ct);
        var budgetLines = await eligibility.GetEligibleBudgetLines(budgetId, periodId, requestedLineIds, ct);
        if (budgetLines.Count != requestedLineIds.Length)
        {
            return CommandResult.NotFound();
        }

        var totalAssigned = assignments.Sum(x => x.Amount);
        if (totalAssigned > transaction.Amount)
        {
            return CommandResult.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(assignments)] = ["Total assigned amount cannot exceed the transaction amount."]
            });
        }

        var existing = await db.TransactionAssignments
            .Where(x => x.TransactionId == transactionId)
            .ToListAsync(ct);
        db.TransactionAssignments.RemoveRange(existing);
        db.TransactionAssignments.AddRange(transaction.ReplaceAssignments(assignments));
        audit.Add(new DomainEvent(
            assignments.Count > 1 ? "TransactionSplit" : "TransactionAssigned",
            budgetId,
            periodId,
            nameof(FinancialTransaction),
            transactionId,
            $"Replaced assignments for transaction {transaction.Description}.",
            $"Previous={FormatAssignments(existing)}; New={FormatAssignments(assignments)}"));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent();
    }

    private async Task<Guid?> FindPeriodIdForDate(Guid budgetId, DateOnly date, CancellationToken ct) =>
        (await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.StartDate <= date && x.EndDate >= date, ct))?.Id;

    private static string FormatAssignments(IEnumerable<TransactionAssignment> assignments) =>
        string.Join("; ", assignments.Select(x => $"{x.BudgetLineId}:{x.Amount}"));

    private static string FormatAssignments(IEnumerable<TransactionAssignmentItem> assignments) =>
        string.Join("; ", assignments.Select(x => $"{x.BudgetLineId}:{x.Amount}"));
}

public sealed class ClearTransactionAssignmentsHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct);
        if (transaction is null)
        {
            return CommandResult.NotFound();
        }

        var assignments = await db.TransactionAssignments
            .Where(x => x.TransactionId == transactionId)
            .ToListAsync(ct);
        db.TransactionAssignments.RemoveRange(assignments);
        var periodId = await FindPeriodIdForDate(budgetId, transaction.TransactionDate, ct);
        audit.Add(new DomainEvent(
            "TransactionAssignmentsCleared",
            budgetId,
            periodId,
            nameof(FinancialTransaction),
            transactionId,
            $"Cleared assignments for transaction {transaction.Description}.",
            FormatAssignments(assignments)));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent();
    }

    private async Task<Guid?> FindPeriodIdForDate(Guid budgetId, DateOnly date, CancellationToken ct) =>
        (await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.StartDate <= date && x.EndDate >= date, ct))?.Id;

    private static string FormatAssignments(IEnumerable<TransactionAssignment> assignments) =>
        string.Join("; ", assignments.Select(x => $"{x.BudgetLineId}:{x.Amount}"));
}

public sealed class PreviewTransactionImportHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult<TransactionImportDetail>> Handle(Guid budgetId, string fileName, string csvContent, CancellationToken ct)
    {
        if (!await db.Budgets.AnyAsync(x => x.Id == budgetId, ct))
        {
            return CommandResult<TransactionImportDetail>.NotFound();
        }

        IReadOnlyList<ParsedImportRow> parsedRows;
        try
        {
            parsedRows = TransactionImportParsing.Parse(csvContent);
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult<TransactionImportDetail>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(csvContent)] = [ex.Message]
            });
        }

        var existingTransactions = await db.Transactions
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .ToListAsync(ct);

        var batch = TransactionImportBatch.Preview(budgetId, fileName, parsedRows.Count);
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
        audit.Add(new DomainEvent(
            "TransactionImportBatchPreviewed",
            budgetId,
            null,
            nameof(TransactionImportBatch),
            batch.Id,
            $"Previewed import batch {batch.FileName} with {batch.RowCount} row(s)."));
        await audit.AddImportBatchPeriodAudits(budgetId, batch.Id, batch.FileName, rows, "TransactionImportBatchPreviewed", "Previewed", ct);
        await db.SaveChangesAsync(ct);

        return CommandResult<TransactionImportDetail>.Created(new TransactionImportDetail(batch, rows));
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

public sealed class CommitTransactionImportHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult<TransactionImportDetail>> Handle(Guid budgetId, Guid batchId, CancellationToken ct)
    {
        var batch = await db.TransactionImportBatches.FirstOrDefaultAsync(x => x.Id == batchId && x.BudgetId == budgetId, ct);
        if (batch is null)
        {
            return CommandResult<TransactionImportDetail>.NotFound();
        }

        var rows = await db.TransactionImportRows
            .Where(x => x.ImportBatchId == batchId)
            .OrderBy(x => x.RowNumber)
            .ToListAsync(ct);

        if (batch.Status == TransactionImportBatchStatus.Committed)
        {
            return CommandResult<TransactionImportDetail>.Success(new TransactionImportDetail(batch, rows));
        }

        foreach (var row in rows)
        {
            var transaction = row.ToTransaction(budgetId, batch.Id);
            db.Transactions.Add(transaction);
            row.MarkCommitted(transaction.Id);

            var periodId = await FindPeriodIdForDate(budgetId, transaction.TransactionDate, ct);
            audit.Add(new DomainEvent(
                "TransactionImported",
                budgetId,
                periodId,
                nameof(FinancialTransaction),
                transaction.Id,
                $"Imported transaction {transaction.Description} from batch {batch.FileName}."));
        }

        batch.Commit(DateTimeOffset.UtcNow);
        audit.Add(new DomainEvent(
            "TransactionImportBatchCommitted",
            budgetId,
            null,
            nameof(TransactionImportBatch),
            batch.Id,
            $"Committed import batch {batch.FileName} with {rows.Count} row(s)."));
        await audit.AddImportBatchPeriodAudits(budgetId, batch.Id, batch.FileName, rows, "TransactionImportBatchCommitted", "Committed", ct);
        await db.SaveChangesAsync(ct);

        return CommandResult<TransactionImportDetail>.Success(new TransactionImportDetail(batch, rows));
    }

    private async Task<Guid?> FindPeriodIdForDate(Guid budgetId, DateOnly date, CancellationToken ct) =>
        (await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.StartDate <= date && x.EndDate >= date, ct))?.Id;
}
