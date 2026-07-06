using System.Globalization;
using System.Text.RegularExpressions;

namespace BudgetyTzar.Api.Features.Budgeting;

public sealed partial record PositiveMoneyAmount
{
    private const decimal MaximumValue = 99999999.99m;

    private PositiveMoneyAmount(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public string FormattedValue => Value.ToString("0.00", CultureInfo.InvariantCulture);

    public static bool TryCreate(string? value, out PositiveMoneyAmount? amount)
    {
        var trimmedValue = value?.Trim() ?? string.Empty;

        if (!AmountPattern().IsMatch(trimmedValue)
            || !decimal.TryParse(trimmedValue, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsedValue)
            || parsedValue <= 0.00m
            || parsedValue > MaximumValue)
        {
            amount = null;
            return false;
        }

        amount = new PositiveMoneyAmount(parsedValue);
        return true;
    }

    [GeneratedRegex(@"^\d{1,8}\.\d{2}$")]
    private static partial Regex AmountPattern();
}
