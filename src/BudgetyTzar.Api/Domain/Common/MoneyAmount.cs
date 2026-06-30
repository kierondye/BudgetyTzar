namespace BudgetyTzar.Api;

public abstract record PositiveMoneyAmountResult
{
    private PositiveMoneyAmountResult()
    {
    }

    public sealed record Success(PositiveMoneyAmount Amount) : PositiveMoneyAmountResult;

    public sealed record ValidationFailed(string Error) : PositiveMoneyAmountResult;
}

public readonly record struct MoneyAmount
{
    public const string PositiveAmountRequiredMessage = "Amount must be greater than zero.";
    public const string MoneyScaleExceededMessage = "Money values must use at most two decimal places.";

    public decimal Value { get; }

    public MoneyAmount(decimal value)
    {
        if (decimal.Round(value, 2) != value)
        {
            throw new ArgumentException(MoneyScaleExceededMessage, nameof(value));
        }

        Value = value;
    }

    public static MoneyAmount Positive(decimal value)
    {
        return new MoneyAmount(PositiveMoneyAmount.Require(value).Value);
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

public sealed record PositiveMoneyAmount
{
    private PositiveMoneyAmount(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public static PositiveMoneyAmountResult Create(decimal value)
    {
        if (value <= 0)
        {
            return new PositiveMoneyAmountResult.ValidationFailed(MoneyAmount.PositiveAmountRequiredMessage);
        }

        if (decimal.Round(value, 2) != value)
        {
            return new PositiveMoneyAmountResult.ValidationFailed(MoneyAmount.MoneyScaleExceededMessage);
        }

        return new PositiveMoneyAmountResult.Success(new PositiveMoneyAmount(value));
    }

    public static PositiveMoneyAmount Require(decimal value)
    {
        var result = Create(value);
        if (result is PositiveMoneyAmountResult.Success success)
        {
            return success.Amount;
        }

        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), MoneyAmount.PositiveAmountRequiredMessage);
        }

        throw new ArgumentException(MoneyAmount.MoneyScaleExceededMessage, nameof(value));
    }
}
