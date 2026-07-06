using System.Globalization;
using System.Text.RegularExpressions;

namespace BudgetyTzar.Api.Features.Transactions;

public readonly partial record struct TransactionAmount
{
    private const decimal MaximumAmount = 99999999.99m;

    public static TransactionAmount Empty { get; } = new(string.Empty);

    private TransactionAmount(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out TransactionAmount amount)
    {
        var rawValue = value ?? string.Empty;

        if (!AmountPattern().IsMatch(rawValue)
            || !decimal.TryParse(rawValue, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsedValue)
            || parsedValue <= 0m
            || parsedValue > MaximumAmount)
        {
            amount = Empty;
            return false;
        }

        amount = new TransactionAmount(rawValue);
        return true;
    }

    [GeneratedRegex(@"^(?:0|[1-9]\d{0,7})\.\d{2}$")]
    private static partial Regex AmountPattern();
}
