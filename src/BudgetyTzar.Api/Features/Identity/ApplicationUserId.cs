namespace BudgetyTzar.Api.Features.Identity;

public sealed record ApplicationUserId
{
    private ApplicationUserId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out ApplicationUserId? userId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            userId = null;
            return false;
        }

        userId = new ApplicationUserId(value.Trim());
        return true;
    }
}
