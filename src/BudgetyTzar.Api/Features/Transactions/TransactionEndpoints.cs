using BudgetyTzar.Api.Features.TransactionAllocations;

namespace BudgetyTzar.Api.Features.Transactions;

public static class TransactionEndpoints
{
    public static IServiceCollection AddTransactions(this IServiceCollection services)
    {
        services.AddSingleton<TransactionStore>();
        return services;
    }

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var transactions = endpoints.MapGroup("/api/transactions")
            .WithTags("Transactions");

        transactions.MapPost("/", CreateTransaction)
            .WithName("CreateTransaction");

        transactions.MapGet("/", GetTransactions)
            .WithName("GetTransactions");

        transactions.MapGet("/{transactionId:guid}", GetTransaction)
            .WithName("GetTransaction");

        transactions.MapDelete("/{transactionId:guid}", DeleteTransaction)
            .WithName("DeleteTransaction");

        return endpoints;
    }

    private static IResult CreateTransaction(CreateTransactionRequest request, TransactionStore store)
    {
        var result = store.Create(
            request.Description,
            request.Type,
            request.TransactionDate,
            request.Amount,
            request.Currency);

        return result switch
        {
            CreateTransactionResult.Invalid invalid => Results.ValidationProblem(invalid.Errors),
            CreateTransactionResult.Created created => Results.Created(
                $"/api/transactions/{created.Transaction.TransactionId}",
                TransactionResponse.FromTransaction(created.Transaction)),
            _ => throw new InvalidOperationException("Unexpected create transaction result.")
        };
    }

    private static IResult GetTransactions(TransactionStore store)
    {
        var transactions = store.GetAll()
            .Select(TransactionListItemResponse.FromTransaction)
            .ToList();

        return Results.Ok(transactions);
    }

    private static IResult GetTransaction(Guid transactionId, TransactionStore store)
    {
        var transaction = store.Get(transactionId);

        return transaction is null
            ? Results.NotFound()
            : Results.Ok(TransactionResponse.FromTransaction(transaction));
    }

    private static IResult DeleteTransaction(
        Guid transactionId,
        TransactionStore store,
        TransactionAllocationStore allocationStore)
    {
        if (store.Get(transactionId) is null)
        {
            return Results.NotFound();
        }

        if (allocationStore.HasAllocationForTransaction(transactionId))
        {
            return Results.Conflict();
        }

        store.Delete(transactionId);
        return Results.NoContent();
    }
}
