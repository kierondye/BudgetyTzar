using System.Globalization;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Common;

namespace BudgetyTzar.Api.Features.Transactions;

public static class TransactionEndpoints
{
    public static IServiceCollection AddTransactions(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryTransactionRepository>();
        services.AddSingleton<InMemoryTransactionAllocationRepository>();
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

    private static IResult CreateTransaction(CreateTransactionRequest request, InMemoryTransactionRepository transactions)
    {
        var validation = Validate(
            request.Description,
            request.Type,
            request.TransactionDate,
            request.Amount,
            request.Currency);

        if (validation is CreateTransactionValidationResult.Invalid invalid)
        {
            return Results.ValidationProblem(invalid.Errors);
        }

        var valid = (CreateTransactionValidationResult.Valid)validation;
        var transaction = Transaction.Record(
            Guid.NewGuid(),
            valid.Description,
            valid.Type,
            valid.TransactionDate,
            valid.Amount,
            valid.Currency);

        transactions.Add(transaction);

        return Results.Created(
            $"/api/transactions/{transaction.TransactionId}",
            TransactionResponse.FromTransaction(transaction));
    }

    private static IResult GetTransactions(
        InMemoryTransactionRepository transactions,
        InMemoryTransactionAllocationRepository allocations,
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
        var response = transactions.GetAll()
            .Where(transaction => valid.Filters.Matches(
                transaction,
                allocations.Get(transaction.TransactionId) is not null))
            .Select(TransactionListItemResponse.FromTransaction)
            .ToList();

        return Results.Ok(response);
    }

    private static IResult GetTransaction(Guid transactionId, InMemoryTransactionRepository transactions)
    {
        var transaction = transactions.Get(transactionId);

        return transaction is null
            ? Results.NotFound()
            : Results.Ok(TransactionResponse.FromTransaction(transaction));
    }

    private static IResult DeleteTransaction(
        Guid transactionId,
        InMemoryTransactionRepository transactions,
        InMemoryTransactionAllocationRepository allocations)
    {
        if (transactions.Get(transactionId) is null)
        {
            return Results.NotFound();
        }

        if (allocations.HasAllocationForTransaction(transactionId))
        {
            return Results.Conflict();
        }

        transactions.Delete(transactionId);
        return Results.NoContent();
    }

    private static IResult AllocateTransaction(
        Guid transactionId,
        AllocateTransactionRequest request,
        InMemoryTransactionRepository transactions,
        InMemoryBudgetRepository budgets,
        InMemoryTransactionAllocationRepository allocations)
    {
        var transaction = transactions.Get(transactionId);

        if (transaction is null)
        {
            return Results.NotFound();
        }

        var budgetItemReference = budgets.GetBudgetItemReference(request.BudgetItemId);

        if (budgetItemReference is null)
        {
            return Results.NotFound();
        }

        if (transaction.Currency != budgetItemReference.BudgetCurrency)
        {
            return Results.Conflict();
        }

        var existingAllocation = allocations.Get(transactionId);

        if (existingAllocation is not null)
        {
            return existingAllocation.BudgetItemId == request.BudgetItemId
                ? Results.Ok(TransactionAllocationResponse.FromAllocation(existingAllocation))
                : Results.Conflict();
        }

        var allocation = TransactionAllocation.Allocate(transaction, request.BudgetItemId);
        allocations.Add(allocation);

        return Results.Ok(TransactionAllocationResponse.FromAllocation(allocation));
    }

    private static IResult GetTransactionAllocation(
        Guid transactionId,
        InMemoryTransactionRepository transactions,
        InMemoryTransactionAllocationRepository allocations)
    {
        if (transactions.Get(transactionId) is null)
        {
            return Results.NotFound();
        }

        var allocation = allocations.Get(transactionId);

        return allocation is null
            ? Results.NotFound()
            : Results.Ok(TransactionAllocationResponse.FromAllocation(allocation));
    }

    private static IResult DeleteTransactionAllocation(
        Guid transactionId,
        InMemoryTransactionRepository transactions,
        InMemoryTransactionAllocationRepository allocations)
    {
        if (transactions.Get(transactionId) is null)
        {
            return Results.NotFound();
        }

        allocations.Remove(transactionId);

        return Results.NoContent();
    }

    private static CreateTransactionValidationResult Validate(
        string description,
        string type,
        string transactionDate,
        string amount,
        string currency)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(description))
        {
            errors["description"] = ["Transaction description is required."];
        }

        if (!TransactionType.TryCreate(type, out var parsedType))
        {
            errors["type"] = ["Transaction type must be Credit or Debit."];
        }

        if (!DateOnly.TryParseExact(
            transactionDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedTransactionDate))
        {
            errors["transactionDate"] = ["Transaction date must use the yyyy-MM-dd format."];
        }

        if (!PositiveMoneyAmount.TryCreate(amount, out var parsedAmount))
        {
            errors["amount"] = ["Amount must be a positive decimal string with exactly two decimal places and no more than 99999999.99."];
        }

        if (!CurrencyCode.TryCreate(currency, out var parsedCurrency))
        {
            errors["currency"] = ["Currency must be an uppercase ISO 4217 alphabetic code."];
        }

        return errors.Count > 0
            ? new CreateTransactionValidationResult.Invalid(errors)
            : new CreateTransactionValidationResult.Valid(
                description.Trim(),
                parsedType,
                parsedTransactionDate,
                parsedAmount!,
                parsedCurrency);
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

    private abstract record CreateTransactionValidationResult
    {
        public sealed record Valid(
            string Description,
            TransactionType Type,
            DateOnly TransactionDate,
            PositiveMoneyAmount Amount,
            CurrencyCode Currency) : CreateTransactionValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : CreateTransactionValidationResult;
    }
}
