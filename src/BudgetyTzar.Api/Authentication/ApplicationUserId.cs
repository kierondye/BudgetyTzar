namespace BudgetyTzar.Api.Authentication;

public readonly record struct ApplicationUserId
{
    private ApplicationUserId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static ApplicationUserId New()
    {
        return new ApplicationUserId(Guid.NewGuid());
    }

    public static ApplicationUserId FromGuid(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Application user identity is required.", nameof(value));
        }

        return new ApplicationUserId(value);
    }
}
