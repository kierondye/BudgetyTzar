using System.Globalization;

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
        var errors = Validate(request, out var type, out var transactionDate, out var amount, out var currency);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var transaction = store.Create(
            request.Description.Trim(),
            type,
            transactionDate,
            amount,
            currency);
        var response = TransactionResponse.FromTransaction(transaction);

        return Results.Created($"/api/transactions/{transaction.TransactionId}", response);
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

    private static Dictionary<string, string[]> Validate(
        CreateTransactionRequest request,
        out TransactionType type,
        out DateOnly transactionDate,
        out TransactionAmount amount,
        out TransactionCurrencyCode currency)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        type = TransactionType.Empty;
        transactionDate = default;
        amount = TransactionAmount.Empty;
        currency = TransactionCurrencyCode.Empty;

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            errors["description"] = ["Transaction description is required."];
        }

        if (!TransactionType.TryCreate(request.Type, out type))
        {
            errors["type"] = ["Transaction type must be Credit or Debit."];
        }

        if (!DateOnly.TryParseExact(
            request.TransactionDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out transactionDate))
        {
            errors["transactionDate"] = ["Transaction date must use the yyyy-MM-dd format."];
        }

        if (!TransactionAmount.TryCreate(request.Amount, out amount))
        {
            errors["amount"] = ["Amount must be a positive decimal string with exactly two decimal places and no more than 99999999.99."];
        }

        if (!TransactionCurrencyCode.TryCreate(request.Currency, out currency))
        {
            errors["currency"] = ["Currency must be an uppercase ISO 4217 alphabetic code."];
        }

        return errors;
    }
}
