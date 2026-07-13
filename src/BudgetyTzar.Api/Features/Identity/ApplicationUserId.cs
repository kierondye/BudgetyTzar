namespace BudgetyTzar.Api.Features.Identity;

public sealed record ApplicationUserId
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

    public static bool TryCreate(Guid value, out ApplicationUserId? userId)
    {
        if (value == Guid.Empty)
        {
            userId = null;
            return false;
        }

        userId = new ApplicationUserId(value);
        return true;
    }
}
