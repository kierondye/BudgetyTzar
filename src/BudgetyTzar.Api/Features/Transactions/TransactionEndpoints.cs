using BudgetyTzar.Api.Features.Budgeting;

namespace BudgetyTzar.Api.Features.Transactions;

public static class TransactionEndpoints
{
    public static IServiceCollection AddTransactions(this IServiceCollection services)
    {
        services.AddSingleton<TransactionStore>();
        services.AddSingleton<TransactionAllocationStore>();
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

        transactions.MapPut("/{transactionId:guid}/allocation", AllocateTransaction)
            .WithName("AllocateTransaction");

        transactions.MapGet("/{transactionId:guid}/allocation", GetTransactionAllocation)
            .WithName("GetTransactionAllocation");

        transactions.MapDelete("/{transactionId:guid}/allocation", DeleteTransactionAllocation)
            .WithName("DeleteTransactionAllocation");

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

    private static IResult DeleteTransaction(Guid transactionId, TransactionStore store)
    {
        return store.Delete(transactionId)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static IResult AllocateTransaction(
        Guid transactionId,
        AllocateTransactionRequest request,
        TransactionStore transactionStore,
        BudgetStore budgetStore,
        TransactionAllocationStore allocationStore)
    {
        var transaction = transactionStore.Get(transactionId);

        if (transaction is null)
        {
            return Results.NotFound();
        }

        var budgetItemReference = budgetStore.GetBudgetItemReference(request.BudgetItemId);

        if (budgetItemReference is null)
        {
            return Results.NotFound();
        }

        if (transaction.Currency != budgetItemReference.BudgetCurrency)
        {
            return Results.Conflict();
        }

        var result = allocationStore.Allocate(transaction, request.BudgetItemId);

        return result switch
        {
            AllocateTransactionResult.Allocated allocated => Results.Ok(
                TransactionAllocationResponse.FromAllocation(allocated.Allocation)),
            AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem => Results.Conflict(),
            _ => throw new InvalidOperationException("Unexpected allocate transaction result.")
        };
    }

    private static IResult GetTransactionAllocation(
        Guid transactionId,
        TransactionStore transactionStore,
        TransactionAllocationStore allocationStore)
    {
        if (transactionStore.Get(transactionId) is null)
        {
            return Results.NotFound();
        }

        var allocation = allocationStore.Get(transactionId);

        return allocation is null
            ? Results.NotFound()
            : Results.Ok(TransactionAllocationResponse.FromAllocation(allocation));
    }

    private static IResult DeleteTransactionAllocation(
        Guid transactionId,
        TransactionStore transactionStore,
        TransactionAllocationStore allocationStore)
    {
        if (transactionStore.Get(transactionId) is null)
        {
            return Results.NotFound();
        }

        allocationStore.Remove(transactionId);

        return Results.NoContent();
    }
}
