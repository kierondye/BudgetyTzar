namespace BudgetyTzar.Api.Features.Identity;

public readonly record struct ApplicationUserId
{
    public static ApplicationUserId DefaultTestUser { get; } = new("test-user");

    private ApplicationUserId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out ApplicationUserId userId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            userId = default;
            return false;
        }

        userId = new ApplicationUserId(value.Trim());
        return true;
    }

    public override string ToString()
    {
        return Value;
    }
}
