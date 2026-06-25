using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapTransactionEndpoints(RouteGroupBuilder budgets)
    {
        budgets.MapGet("/{budgetId:guid}/transactions", async (
            Guid budgetId,
            DateOnly? from,
            DateOnly? to,
            string? allocationStatus,
            BudgetDbContext db,
            CancellationToken ct) =>
        {
            if (!await BudgetExists(db, budgetId, ct))
            {
                return Results.NotFound();
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

            TransactionAllocationStatus? parsedAllocationStatus = null;
            if (!string.IsNullOrWhiteSpace(allocationStatus))
            {
                if (!Enum.TryParse<TransactionAllocationStatus>(allocationStatus, ignoreCase: true, out var parsed))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(allocationStatus)] = ["Allocation status must be unallocated, partiallyAllocated, or fullyAllocated."]
                    });
                }

                parsedAllocationStatus = parsed;
            }

            var transactions = await query.OrderByDescending(x => x.TransactionDate).ToListAsync(ct);
            if (!parsedAllocationStatus.HasValue)
            {
                return Results.Ok(transactions);
            }

            var transactionIds = transactions.Select(x => x.Id).ToArray();
            var allocationTotals = await db.TransactionAllocations
                .AsNoTracking()
                .Where(x => transactionIds.Contains(x.TransactionId))
                .GroupBy(x => x.TransactionId)
                .Select(x => new { TransactionId = x.Key, Amount = x.Sum(y => y.Amount) })
                .ToDictionaryAsync(x => x.TransactionId, x => x.Amount, ct);

            var filtered = transactions
                .Where(x => GetAllocationStatus(x, allocationTotals.GetValueOrDefault(x.Id)) == parsedAllocationStatus.Value)
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

            var allocations = await db.TransactionAllocations
                .AsNoTracking()
                .Where(x => x.TransactionId == transactionId)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);
            return Results.Ok(new TransactionDetail(transaction, allocations));
        });

        budgets.MapPost("/{budgetId:guid}/transactions", async (
            Guid budgetId,
            CreateTransactionRequest request,
            IValidator<CreateTransactionRequest> validator,
            CreateTransactionHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(
                budgetId,
                request.TransactionDate,
                request.Description,
                request.Amount,
                request.Direction,
                request.SourceAccount,
                request.ExternalReference,
                request.Notes,
                ct);
            return result.ToHttpResult(httpContext, transaction => Results.Created($"/api/budgets/{budgetId}/transactions/{transaction.Id}", transaction));
        });

        budgets.MapPut("/{budgetId:guid}/transactions/{transactionId:guid}", async (
            Guid budgetId,
            Guid transactionId,
            UpdateTransactionRequest request,
            IValidator<UpdateTransactionRequest> validator,
            UpdateTransactionHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(
                budgetId,
                transactionId,
                request.TransactionDate,
                request.Description,
                request.Amount,
                request.Direction,
                request.SourceAccount,
                request.ExternalReference,
                request.Notes,
                ct);
            return result.ToHttpResult(httpContext, budgetId);
        });

        budgets.MapPost("/{budgetId:guid}/transactions/{transactionId:guid}/ignore", async (
            Guid budgetId,
            Guid transactionId,
            IgnoreTransactionHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(budgetId, transactionId, ct);
            return result.ToHttpResult(httpContext, budgetId);
        });

        budgets.MapGet("/{budgetId:guid}/transactions/{transactionId:guid}/allocations", async (
            Guid budgetId,
            Guid transactionId,
            BudgetDbContext db,
            CancellationToken ct) => await GetTransactionAllocations(budgetId, transactionId, db, ct));

        budgets.MapPut("/{budgetId:guid}/transactions/{transactionId:guid}/allocations", async (
            Guid budgetId,
            Guid transactionId,
            ReplaceTransactionAllocationsRequest request,
            IValidator<ReplaceTransactionAllocationsRequest> validator,
            ReplaceTransactionAllocationsHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(budgetId, transactionId, request.Allocations, ct);
            return result.ToHttpResult(httpContext, budgetId);
        });

        budgets.MapDelete("/{budgetId:guid}/transactions/{transactionId:guid}/allocations", async (
            Guid budgetId,
            Guid transactionId,
            ClearTransactionAllocationsHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(budgetId, transactionId, ct);
            return result.ToHttpResult(httpContext, budgetId);
        });
    }

    private static async Task<IResult> GetTransactionAllocations(
        Guid budgetId,
        Guid transactionId,
        BudgetDbContext db,
        CancellationToken ct)
    {
        if (!await db.Transactions.AnyAsync(x => x.Id == transactionId && x.BudgetId == budgetId, ct))
        {
            return Results.NotFound();
        }

        var allocations = await db.TransactionAllocations
            .AsNoTracking()
            .Where(x => x.TransactionId == transactionId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        return Results.Ok(allocations);
    }
}
