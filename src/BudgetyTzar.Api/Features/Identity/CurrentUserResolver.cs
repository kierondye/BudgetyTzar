using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Features.Identity;

public sealed class CurrentUserResolver(
    IOptions<CurrentUserResolverOptions> options,
    IApplicationUserStore users)
{
    private readonly IReadOnlyList<string> userIdClaimTypes = options.Value.UserIdClaimTypes.Count == 0
        ? CurrentUserResolverOptions.DefaultUserIdClaimTypes
        : options.Value.UserIdClaimTypes;

    public CurrentUserResolution Resolve(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return new CurrentUserResolution.Unauthenticated();
        }

        var subjectClaim = ResolveSubjectClaim(principal);
        var subject = subjectClaim?.Value;
        var provider = ResolveProvider(principal, subjectClaim);

        if (!ExternalIdentity.TryCreate(provider, subject, out var externalIdentity))
        {
            return new CurrentUserResolution.Unauthenticated();
        }

        var applicationUserKey = ApplicationUserKey.FromExternalIdentity(externalIdentity!);
        var applicationUserId = users.GetOrCreateApplicationUserId(applicationUserKey);

        return new CurrentUserResolution.Authenticated(new CurrentUser(applicationUserId));
    }

    private Claim? ResolveSubjectClaim(ClaimsPrincipal principal)
    {
        foreach (var claimType in userIdClaimTypes)
        {
            var claim = principal.FindFirst(claimType);

            if (!string.IsNullOrWhiteSpace(claim?.Value))
            {
                return claim;
            }
        }

        return null;
    }

    private static string? ResolveProvider(ClaimsPrincipal principal, Claim? subjectClaim)
    {
        if (!string.IsNullOrWhiteSpace(subjectClaim?.Issuer)
            && subjectClaim.Issuer != ClaimsIdentity.DefaultIssuer)
        {
            return subjectClaim.Issuer;
        }

        return principal.Identity?.AuthenticationType;
    }
}

public sealed class CurrentUserResolverOptions
{
    public static readonly IReadOnlyList<string> DefaultUserIdClaimTypes =
        [ClaimTypes.NameIdentifier, "sub"];

    public IReadOnlyList<string> UserIdClaimTypes { get; set; } = DefaultUserIdClaimTypes;
}

public abstract record CurrentUserResolution
{
    public sealed record Authenticated(CurrentUser User) : CurrentUserResolution;

    public sealed record Unauthenticated : CurrentUserResolution;
}
