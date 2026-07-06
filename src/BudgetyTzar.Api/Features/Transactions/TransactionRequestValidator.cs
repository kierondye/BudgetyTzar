using System.Globalization;

namespace BudgetyTzar.Api.Features.Transactions;

public static class TransactionRequestValidator
{
    public static Dictionary<string, string[]> ValidateCreateRequest(
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
