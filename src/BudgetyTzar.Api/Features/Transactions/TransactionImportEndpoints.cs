using BudgetyTzar.Api.Application.Transactions;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record PreviewTransactionImportRequest(string FileName, string CsvContent);
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
            PreviewTransactionImportHandler handler,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(budgetId, request.FileName, request.CsvContent, ct);
            return result.ToHttpResult(detail => Results.Created($"/api/budgets/{budgetId}/transaction-imports/{detail.Batch.Id}", detail));
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
            CommitTransactionImportHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(budgetId, batchId, ct);
            return result.ToHttpResult();
        });
    }
}
