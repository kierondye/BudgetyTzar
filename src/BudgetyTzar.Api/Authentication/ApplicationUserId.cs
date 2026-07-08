using System.Security.Claims;

namespace BudgetyTzar.Api.Authentication;

public readonly record struct ApplicationUserId
{
    private ApplicationUserId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ApplicationUserId FromPrincipal(ClaimsPrincipal principal, string claimType)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var value = principal.FindFirstValue(claimType);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The authenticated principal does not contain the configured '{claimType}' user identity claim.");
        }

        return new ApplicationUserId(value);
    }
}
