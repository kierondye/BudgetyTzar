using System.Globalization;
using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Audit;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Observability;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BudgetyTzar.Api.Features.Transactions;

public static class TransactionEndpoints
{
    public static IServiceCollection AddTransactions(this IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryDataStore>();
        services.AddScoped<InMemoryTransactionRepository>();
        services.AddScoped<ITransactionRepository>(provider => provider.GetRequiredService<InMemoryTransactionRepository>());
        services.AddScoped<InMemoryTransactionAllocationRepository>();
        services.AddScoped<ITransactionAllocationRepository>(provider =>
            provider.GetRequiredService<InMemoryTransactionAllocationRepository>());
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
        ITransactionRepository transactions,
        IAuditRecorder audit,
        ApiTelemetry telemetry)
    {
        var validation = Validate(
            request.Description,
            request.Type,
            request.TransactionDate,
            request.Amount,
            request.Currency);

        if (validation is CreateTransactionValidationResult.Invalid invalid)
        {
            telemetry.RecordValidationFailure("CreateTransaction", "request_validation");
            return Results.ValidationProblem(invalid.Errors);
        }

        var valid = (CreateTransactionValidationResult.Valid)validation;
        return Transaction.Create(
            Guid.NewGuid(),
            valid.Description,
            valid.Type,
            valid.TransactionDate,
            valid.Amount,
            valid.Currency) switch
        {
            CreateTransactionResult.InvalidIdentity => Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["transactionId"] = ["Transaction identity is required."]
                }),
            CreateTransactionResult.InvalidDescription => Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["description"] = ["Transaction description is required."]
                }),
            CreateTransactionResult.Created created => RecordTransaction(created.Transaction),
            _ => throw new InvalidOperationException("Unexpected create transaction result.")
        };

        IResult RecordTransaction(Transaction transaction)
        {
            transactions.Add(transaction);
            audit.Record(AuditEntry.TransactionCreated(transaction));

            return Results.Created(
                $"/api/transactions/{transaction.TransactionId}",
                TransactionResponse.FromTransaction(transaction));
        }
    }

    private static IResult GetTransactions(
        ITransactionRepository transactions,
        ITransactionAllocationRepository allocations,
        string? from,
        string? to,
        string? allocationStatus,
        ApiTelemetry telemetry)
    {
        var validation = ValidateFilters(from, to, allocationStatus);

        if (validation is TransactionFilterValidationResult.Invalid invalid)
        {
            telemetry.RecordValidationFailure("GetTransactions", "request_validation");
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

    private static IResult GetTransaction(Guid transactionId, ITransactionRepository transactions)
    {
        var transaction = transactions.Get(transactionId);

        return transaction is null
            ? Results.NotFound()
            : Results.Ok(TransactionResponse.FromTransaction(transaction));
    }

    private static IResult DeleteTransaction(
        Guid transactionId,
        ITransactionRepository transactions,
        IAuditRecorder audit)
    {
        var transaction = transactions.Get(transactionId);

        if (transaction is null)
        {
            return Results.NotFound();
        }

        return transactions.Delete(transactionId) switch
        {
            TransactionDeleteResult.NotFound => Results.NotFound(),
            TransactionDeleteResult.TransactionHasAllocation => TransactionHasAllocation(),
            TransactionDeleteResult.Deleted => DeletedTransaction(transaction),
            _ => throw new InvalidOperationException("Unexpected delete transaction result.")
        };

        IResult DeletedTransaction(Transaction deleted)
        {
            audit.Record(AuditEntry.TransactionDeleted(deleted));
            return Results.NoContent();
        }
    }

    private static IResult AllocateTransaction(
        Guid transactionId,
        AllocateTransactionRequest request,
        ITransactionRepository transactions,
        IBudgetRepository budgets,
        ITransactionAllocationRepository allocations,
        IAuditRecorder audit,
        ApiTelemetry telemetry)
    {
        var transaction = transactions.Get(transactionId);

        if (transaction is null)
        {
            telemetry.RecordAllocationFailure("transaction_not_found");
            return Results.NotFound();
        }

        var budgetItemReference = budgets.GetBudgetItemReference(request.BudgetItemId);

        if (budgetItemReference is null)
        {
            telemetry.RecordAllocationFailure("budget_item_not_found");
            return Results.NotFound();
        }

        if (transaction.Currency != budgetItemReference.BudgetCurrency)
        {
            telemetry.RecordAllocationFailure("currency_mismatch");
            return TransactionCurrencyDoesNotMatchBudget();
        }

        var existingAllocation = allocations.Get(transactionId);
        var allocationResult = TransactionAllocation.Allocate(transaction, request.BudgetItemId);

        if (allocationResult is AllocateTransactionEntityResult.InvalidBudgetItemIdentity)
        {
            telemetry.RecordAllocationFailure("budget_item_not_found");
            return Results.NotFound();
        }

        var allocation = ((AllocateTransactionEntityResult.Allocated)allocationResult).Allocation;
        var result = allocations.Allocate(allocation);

        return result switch
        {
            AllocateTransactionResult.Allocated allocated => AllocatedTransaction(allocated.Allocation, existingAllocation),
            AllocateTransactionResult.TransactionNotFound => AllocationFailed(
                telemetry,
                "transaction_not_found",
                Results.NotFound()),
            AllocateTransactionResult.BudgetItemNotFound => AllocationFailed(
                telemetry,
                "budget_item_not_found",
                Results.NotFound()),
            AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem => AllocationFailed(
                telemetry,
                "already_allocated_to_different_budget_item",
                TransactionAlreadyAllocated()),
            _ => throw new InvalidOperationException("Unexpected allocate transaction result.")
        };

        IResult AllocatedTransaction(TransactionAllocation allocation, TransactionAllocation? existing)
        {
            audit.Record(existing is null
                ? AuditEntry.TransactionAllocationCreated(allocation)
                : AuditEntry.TransactionAllocationIdempotent(allocation));
            return Results.Ok(TransactionAllocationResponse.FromAllocation(allocation));
        }
    }

    private static IResult AllocationFailed(ApiTelemetry telemetry, string failureKind, IResult result)
    {
        telemetry.RecordAllocationFailure(failureKind);
        return result;
    }

    private static IResult GetTransactionAllocation(
        Guid transactionId,
        ITransactionRepository transactions,
        ITransactionAllocationRepository allocations)
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
        ITransactionRepository transactions,
        ITransactionAllocationRepository allocations,
        IAuditRecorder audit)
    {
        if (transactions.Get(transactionId) is null)
        {
            return Results.NotFound();
        }

        var allocation = allocations.Get(transactionId);
        allocations.Remove(transactionId);

        if (allocation is not null)
        {
            audit.Record(AuditEntry.TransactionAllocationRemoved(allocation));
        }

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
