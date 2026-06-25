namespace BudgetyTzar.Api;

public readonly record struct Currency
{
    public string Value { get; }

    public Currency(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!IsValid(normalized))
        {
            throw new ArgumentException("Currency must be a three-letter uppercase ISO code.", nameof(value));
        }

        Value = normalized;
    }

    public static bool IsValid(string value) =>
        value.Length == 3 && value.All(char.IsUpper);
}
