using FluentValidation;

namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static void MapTransactionEndpoints(RouteGroupBuilder budgets)
    {
        MapListTransactionsEndpoint(budgets);
        MapGetTransactionEndpoint(budgets);

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

        MapGetTransactionAllocationsEndpoint(budgets);

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
}
