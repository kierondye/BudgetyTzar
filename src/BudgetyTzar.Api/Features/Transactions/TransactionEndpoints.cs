using System.Globalization;
using System.Security.Claims;
using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BudgetyTzar.Api.Features.Transactions;

public static class TransactionEndpoints
{
    public static IServiceCollection AddTransactions(this IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryDataStore>();
        services.AddSingleton<InMemoryTransactionRepository>();
        services.AddSingleton<InMemoryTransactionAllocationRepository>();
        return services;
    }

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var transactions = endpoints.MapGroup("/api/transactions")
            .WithTags("Transactions")
            .RequireAuthorization();

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

    private static IResult CreateTransaction(
        CreateTransactionRequest request,
        ClaimsPrincipal user,
        InMemoryTransactionRepository transactions)
    {
        var currentUser = CurrentUser.FromPrincipal(user);
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
        return Transaction.Record(
            Guid.NewGuid(),
            valid.Description,
            valid.Type,
            valid.TransactionDate,
            valid.Amount,
            valid.Currency) switch
        {
            RecordTransactionResult.InvalidIdentity => Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["transactionId"] = ["Transaction identity is required."]
                }),
            RecordTransactionResult.InvalidDescription => Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["description"] = ["Transaction description is required."]
                }),
            RecordTransactionResult.Recorded recorded => RecordTransaction(recorded.Transaction),
            _ => throw new InvalidOperationException("Unexpected record transaction result.")
        };

        IResult RecordTransaction(Transaction transaction)
        {
            transactions.Add(currentUser.UserId, transaction);

            return Results.Created(
                $"/api/transactions/{transaction.TransactionId}",
                TransactionResponse.FromTransaction(transaction));
        }
    }

    private static IResult GetTransactions(
        InMemoryTransactionRepository transactions,
        InMemoryTransactionAllocationRepository allocations,
        ClaimsPrincipal user,
        string? from,
        string? to,
        string? allocationStatus)
    {
        var currentUser = CurrentUser.FromPrincipal(user);
        var validation = ValidateFilters(from, to, allocationStatus);

        if (validation is TransactionFilterValidationResult.Invalid invalid)
        {
            return Results.ValidationProblem(invalid.Errors);
        }

        var valid = (TransactionFilterValidationResult.Valid)validation;
        var response = transactions.GetAll(currentUser.UserId)
            .Where(transaction => valid.Filters.Matches(
                transaction,
                allocations.Get(currentUser.UserId, transaction.TransactionId) is not null))
            .Select(TransactionListItemResponse.FromTransaction)
            .ToList();

        return Results.Ok(response);
    }

    private static IResult GetTransaction(
        Guid transactionId,
        ClaimsPrincipal user,
        InMemoryTransactionRepository transactions)
    {
        var currentUser = CurrentUser.FromPrincipal(user);
        var transaction = transactions.Get(currentUser.UserId, transactionId);

        return transaction is null
            ? Results.NotFound()
            : Results.Ok(TransactionResponse.FromTransaction(transaction));
    }

    private static IResult DeleteTransaction(
        Guid transactionId,
        ClaimsPrincipal user,
        InMemoryTransactionRepository transactions)
    {
        var currentUser = CurrentUser.FromPrincipal(user);

        return transactions.Delete(currentUser.UserId, transactionId) switch
        {
            TransactionDeleteResult.NotFound => Results.NotFound(),
            TransactionDeleteResult.TransactionHasAllocation => TransactionHasAllocation(),
            TransactionDeleteResult.Deleted => Results.NoContent(),
            _ => throw new InvalidOperationException("Unexpected delete transaction result.")
        };
    }

    private static IResult AllocateTransaction(
        Guid transactionId,
        AllocateTransactionRequest request,
        ClaimsPrincipal user,
        InMemoryTransactionRepository transactions,
        InMemoryBudgetRepository budgets,
        InMemoryTransactionAllocationRepository allocations)
    {
        var currentUser = CurrentUser.FromPrincipal(user);
        var transaction = transactions.Get(currentUser.UserId, transactionId);

        if (transaction is null)
        {
            return Results.NotFound();
        }

        var budgetItemReference = budgets.GetBudgetItemReference(currentUser.UserId, request.BudgetItemId);

        if (budgetItemReference is null)
        {
            return Results.NotFound();
        }

        if (transaction.Currency != budgetItemReference.BudgetCurrency)
        {
            return TransactionCurrencyDoesNotMatchBudget();
        }

        var allocationResult = TransactionAllocation.Allocate(transaction, request.BudgetItemId);

        if (allocationResult is AllocateTransactionEntityResult.InvalidBudgetItemIdentity)
        {
            return Results.NotFound();
        }

        var allocation = ((AllocateTransactionEntityResult.Allocated)allocationResult).Allocation;
        var result = allocations.Allocate(currentUser.UserId, allocation);

        return result switch
        {
            AllocateTransactionResult.Allocated allocated => Results.Ok(
                TransactionAllocationResponse.FromAllocation(allocated.Allocation)),
            AllocateTransactionResult.TransactionNotFound => Results.NotFound(),
            AllocateTransactionResult.BudgetItemNotFound => Results.NotFound(),
            AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem => TransactionAlreadyAllocated(),
            _ => throw new InvalidOperationException("Unexpected allocate transaction result.")
        };
    }

    private static IResult GetTransactionAllocation(
        Guid transactionId,
        ClaimsPrincipal user,
        InMemoryTransactionRepository transactions,
        InMemoryTransactionAllocationRepository allocations)
    {
        var currentUser = CurrentUser.FromPrincipal(user);

        if (transactions.Get(currentUser.UserId, transactionId) is null)
        {
            return Results.NotFound();
        }

        var allocation = allocations.Get(currentUser.UserId, transactionId);

        return allocation is null
            ? Results.NotFound()
            : Results.Ok(TransactionAllocationResponse.FromAllocation(allocation));
    }

    private static IResult DeleteTransactionAllocation(
        Guid transactionId,
        ClaimsPrincipal user,
        InMemoryTransactionRepository transactions,
        InMemoryTransactionAllocationRepository allocations)
    {
        var currentUser = CurrentUser.FromPrincipal(user);

        if (transactions.Get(currentUser.UserId, transactionId) is null)
        {
            return Results.NotFound();
        }

        allocations.Remove(currentUser.UserId, transactionId);

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

    private static IResult TransactionHasAllocation()
    {
        return Results.Conflict(new ConflictResponse(
            "TransactionHasAllocation",
            "Transaction has an allocation."));
    }

    private static IResult TransactionCurrencyDoesNotMatchBudget()
    {
        return Results.Conflict(new ConflictResponse(
            "TransactionCurrencyDoesNotMatchBudget",
            "Transaction currency does not match the budget currency."));
    }

    private static IResult TransactionAlreadyAllocated()
    {
        return Results.Conflict(new ConflictResponse(
            "TransactionAlreadyAllocated",
            "Transaction is already allocated to a different budget item."));
    }

    private sealed record ConflictResponse(string Code, string Message);

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
