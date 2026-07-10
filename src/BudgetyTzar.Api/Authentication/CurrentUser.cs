using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Authentication;

public interface ICurrentUser
{
    ApplicationUserId UserId { get; }
}

public sealed class CurrentUser : ICurrentUser
{
    private ApplicationUserId? userId;

    public ApplicationUserId UserId
    {
        get
        {
            return userId
                ?? throw new InvalidOperationException(
                    "The current user was not resolved before application logic ran.");
        }
    }

    public void Set(ApplicationUserId resolvedUserId)
    {
        userId = resolvedUserId;
    }
}

public sealed class CurrentUserResolver(
    IOptions<AuthenticationOptions> options,
    InMemoryExternalIdentityStore identityStore)
{
    public ResolveCurrentUserResult Resolve(ClaimsPrincipal principal)
    {
        var authentication = options.Value;

        if (!ExternalUserIdentity.TryCreate(
                principal.FindFirstValue(authentication.ProviderClaimType),
                principal.FindFirstValue(authentication.SubjectClaimType),
                out var externalIdentity))
        {
            return new ResolveCurrentUserResult.InvalidExternalIdentity();
        }

        return new ResolveCurrentUserResult.Resolved(
            identityStore.GetOrCreateUserId(externalIdentity));
    }
}

public abstract record ResolveCurrentUserResult
{
    public sealed record Resolved(ApplicationUserId UserId) : ResolveCurrentUserResult;

    public sealed record InvalidExternalIdentity : ResolveCurrentUserResult;
}
