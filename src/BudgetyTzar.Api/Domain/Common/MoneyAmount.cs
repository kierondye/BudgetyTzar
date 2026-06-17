namespace BudgetyTzar.Api;

public readonly record struct MoneyAmount
{
    public decimal Value { get; }

    public MoneyAmount(decimal value)
    {
        if (decimal.Round(value, 2) != value)
        {
            throw new ArgumentException("Money values must use at most two decimal places.", nameof(value));
        }

        Value = value;
    }

    public static MoneyAmount Positive(decimal value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Amount must be greater than zero.");
        }

        return new MoneyAmount(value);
    }

    public static MoneyAmount NonZero(decimal value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Amount must not be zero.");
        }

        return new MoneyAmount(value);
    }
}
