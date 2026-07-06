using System.Globalization;
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

    private static IResult GetTransactions(
        TransactionStore store,
        TransactionAllocationStore allocationStore,
        string? from,
        string? to,
        string? allocationStatus)
    {
        var validation = ValidateFilters(from, to, allocationStatus);

        if (validation is TransactionFilterValidationResult.Invalid invalid)
        {
            return Results.ValidationProblem(invalid.Errors);
        }

        var valid = (TransactionFilterValidationResult.Valid)validation;
        var transactions = store.GetAll(
                valid.Filters,
                transactionId => allocationStore.Get(transactionId) is not null)
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

    private static TransactionFilterValidationResult ValidateFilters(
        string? from,
        string? to,
        string? allocationStatus)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        DateOnly? parsedFrom = null;
        DateOnly? parsedTo = null;
        var parsedAllocationStatus = TransactionAllocationStatus.All;

        if (from is not null)
        {
            if (TryParseDateFilter(from, out var filterDate))
            {
                parsedFrom = filterDate;
            }
            else
            {
                errors["from"] = ["From date must use the yyyy-MM-dd format."];
            }
        }

        if (to is not null)
        {
            if (TryParseDateFilter(to, out var filterDate))
            {
                parsedTo = filterDate;
            }
            else
            {
                errors["to"] = ["To date must use the yyyy-MM-dd format."];
            }
        }

        if (!TransactionAllocationStatus.TryCreate(allocationStatus, out parsedAllocationStatus))
        {
            errors["allocationStatus"] = ["Allocation status must be allocated, unallocated, or all."];
        }

        return errors.Count > 0
            ? new TransactionFilterValidationResult.Invalid(errors)
            : new TransactionFilterValidationResult.Valid(new TransactionFilters(parsedFrom, parsedTo, parsedAllocationStatus));
    }

    private static bool TryParseDateFilter(string value, out DateOnly date)
    {
        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private abstract record TransactionFilterValidationResult
    {
        public sealed record Valid(TransactionFilters Filters) : TransactionFilterValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : TransactionFilterValidationResult;
    }
}
