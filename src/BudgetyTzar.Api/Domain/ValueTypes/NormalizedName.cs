namespace BudgetyTzar.Api.Domain.ValueTypes;

public readonly record struct NormalizedName
{
    private NormalizedName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out NormalizedName name)
    {
        var normalizedValue = value?.Trim() ?? string.Empty;

        if (normalizedValue.Length == 0)
        {
            name = default;
            return false;
        }

        name = new NormalizedName(normalizedValue);
        return true;
    }

    public override string ToString()
    {
        return Value;
    }
}
