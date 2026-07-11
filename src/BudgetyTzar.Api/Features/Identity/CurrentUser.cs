using System.Security.Claims;

namespace BudgetyTzar.Api.Features.Identity;

public sealed record CurrentUser(ApplicationUserId UserId)
{
    public static CurrentUser FromPrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");

        if (!ApplicationUserId.TryCreate(subject, out var userId))
        {
            throw new InvalidOperationException("Authenticated request is missing a stable user identity claim.");
        }

        return new CurrentUser(userId);
    }
}
