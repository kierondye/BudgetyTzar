using System.Text.RegularExpressions;

namespace BudgetyTzar.Api.Features.Transactions;

public readonly partial record struct TransactionCurrencyCode
{
    public static TransactionCurrencyCode Empty { get; } = new(string.Empty);

    private TransactionCurrencyCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out TransactionCurrencyCode currency)
    {
        var trimmedValue = value?.Trim() ?? string.Empty;

        if (!CurrencyCodePattern().IsMatch(trimmedValue))
        {
            currency = Empty;
            return false;
        }

        currency = new TransactionCurrencyCode(trimmedValue);
        return true;
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCodePattern();
}
