using System.Globalization;
using System.Security.Claims;

namespace BudgetyTzar.Api.Features.Identity;

public sealed class CurrentUserResolver
{
    public CurrentUserResolution Resolve(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return new CurrentUserResolution.Unauthenticated();
        }

        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        var provider = ResolveProvider(principal);

        if (!ExternalIdentity.TryCreate(provider, subject, out var externalIdentity))
        {
            return new CurrentUserResolution.Unauthenticated();
        }

        var applicationUserValue = CreateApplicationUserValue(externalIdentity!);

        return ApplicationUserId.TryCreate(applicationUserValue, out var userId)
            ? new CurrentUserResolution.Authenticated(new CurrentUser(userId!))
            : new CurrentUserResolution.Unauthenticated();
    }

    private static string? ResolveProvider(ClaimsPrincipal principal)
    {
        var subjectClaim = principal.FindFirst(ClaimTypes.NameIdentifier)
            ?? principal.FindFirst("sub");

        if (!string.IsNullOrWhiteSpace(subjectClaim?.Issuer)
            && subjectClaim.Issuer != ClaimsIdentity.DefaultIssuer)
        {
            return subjectClaim.Issuer;
        }

        return principal.Identity?.AuthenticationType;
    }

    private static string CreateApplicationUserValue(ExternalIdentity externalIdentity)
    {
        var provider = externalIdentity.Provider;
        var subject = externalIdentity.Subject;

        return string.Concat(
            provider.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            provider,
            subject.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            subject);
    }
}

public abstract record CurrentUserResolution
{
    public sealed record Authenticated(CurrentUser User) : CurrentUserResolution;

    public sealed record Unauthenticated : CurrentUserResolution;
}
