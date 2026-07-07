using System.Text.RegularExpressions;

namespace BudgetyTzar.Api.Domain.ValueTypes;

public readonly partial record struct CurrencyCode
{
    public static CurrencyCode Empty { get; } = new(string.Empty);

    private CurrencyCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out CurrencyCode currency)
    {
        var trimmedValue = value?.Trim() ?? string.Empty;

        if (!CurrencyCodePattern().IsMatch(trimmedValue))
        {
            currency = Empty;
            return false;
        }

        currency = new CurrencyCode(trimmedValue);
        return true;
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCodePattern();
}
