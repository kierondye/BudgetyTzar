using BudgetyTzar.Api.Application.Transactions;
using BudgetyTzar.Api.Infrastructure.Persistence;
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
public sealed record ReplaceTransactionAllocationsRequest(IReadOnlyList<TransactionAssignmentItem> Allocations);
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

public sealed class ReplaceTransactionAllocationsValidator : AbstractValidator<ReplaceTransactionAllocationsRequest>
{
    public ReplaceTransactionAllocationsValidator()
    {
        RuleFor(x => x.Allocations).NotNull();
        RuleForEach(x => x.Allocations).ChildRules(item =>
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
            CreateTransactionHandler handler,
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
            return result.ToHttpResult(transaction => Results.Created($"/api/budgets/{budgetId}/transactions/{transaction.Id}", transaction));
        });

        budgets.MapPut("/{budgetId:guid}/transactions/{transactionId:guid}", async (
            Guid budgetId,
            Guid transactionId,
            UpdateTransactionRequest request,
            IValidator<UpdateTransactionRequest> validator,
            UpdateTransactionHandler handler,
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
            return result.ToHttpResult();
        });

        budgets.MapPost("/{budgetId:guid}/transactions/{transactionId:guid}/ignore", async (
            Guid budgetId,
            Guid transactionId,
            IgnoreTransactionHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(budgetId, transactionId, ct);
            return result.ToHttpResult();
        });

        budgets.MapGet("/{budgetId:guid}/transactions/{transactionId:guid}/allocations", async (
            Guid budgetId,
            Guid transactionId,
            BudgetDbContext db,
            CancellationToken ct) => await GetTransactionAssignments(budgetId, transactionId, db, ct));

        budgets.MapPut("/{budgetId:guid}/transactions/{transactionId:guid}/allocations", async (
            Guid budgetId,
            Guid transactionId,
            ReplaceTransactionAllocationsRequest request,
            IValidator<ReplaceTransactionAllocationsRequest> validator,
            ReplaceTransactionAssignmentsHandler handler,
            CancellationToken ct) =>
        {
            if (await validator.Validate(request, ct) is { } validationProblem)
            {
                return validationProblem;
            }

            var result = await handler.Handle(budgetId, transactionId, request.Allocations, ct);
            return result.ToHttpResult();
        });

        budgets.MapDelete("/{budgetId:guid}/transactions/{transactionId:guid}/allocations", async (
            Guid budgetId,
            Guid transactionId,
            ClearTransactionAssignmentsHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(budgetId, transactionId, ct);
            return result.ToHttpResult();
        });
    }

    private static async Task<IResult> GetTransactionAssignments(
        Guid budgetId,
        Guid transactionId,
        BudgetDbContext db,
        CancellationToken ct)
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
    }
}
