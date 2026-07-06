using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Api.Features.TransactionAllocations;

public static class TransactionAllocationEndpoints
{
    public static IServiceCollection AddTransactionAllocations(this IServiceCollection services)
    {
        services.AddSingleton<TransactionAllocationStore>();
        return services;
    }

    public static IEndpointRouteBuilder MapTransactionAllocationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var allocations = endpoints.MapGroup("/api/transactions/{transactionId:guid}/allocation")
            .WithTags("Transaction Allocations");

        allocations.MapPut("/", AllocateTransaction)
            .WithName("AllocateTransaction");

        allocations.MapGet("/", GetTransactionAllocation)
            .WithName("GetTransactionAllocation");

        allocations.MapDelete("/", RemoveTransactionAllocation)
            .WithName("RemoveTransactionAllocation");

        return endpoints;
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

        var budgetItem = budgetStore.GetBudgetItemReference(request.BudgetItemId);

        if (budgetItem is null)
        {
            return Results.NotFound();
        }

        if (budgetItem.Currency != transaction.Currency)
        {
            return Results.Conflict();
        }

        var result = allocationStore.Allocate(transactionId, request.BudgetItemId);

        return result switch
        {
            AllocateTransactionResult.Allocated allocated => Results.Ok(TransactionAllocationResponse.FromAllocation(allocated.Allocation)),
            AllocateTransactionResult.AlreadyAllocatedToAnotherBudgetItem => Results.Conflict(),
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

    private static IResult RemoveTransactionAllocation(
        Guid transactionId,
        TransactionStore transactionStore,
        TransactionAllocationStore allocationStore)
    {
        if (transactionStore.Get(transactionId) is null)
        {
            return Results.NotFound();
        }

        return allocationStore.Remove(transactionId)
            ? Results.NoContent()
            : Results.NotFound();
    }
}
