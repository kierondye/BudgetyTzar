using System.Diagnostics.CodeAnalysis;

namespace BudgetyTzar.Api.Authentication;

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

    public static bool TryCreate(
        Guid value,
        [NotNullWhen(true)] out ApplicationUserId? userId)
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
