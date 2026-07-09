using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Authentication;

public interface ICurrentUser
{
    ApplicationUserId UserId { get; }
}

public sealed class CurrentUser(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AuthenticationOptions> options,
    InMemoryExternalIdentityStore identityStore)
    : ICurrentUser
{
    private ApplicationUserId? userId;

    public ApplicationUserId UserId
    {
        get
        {
            userId ??= identityStore.GetOrCreateUserId(GetExternalIdentity());
            return userId.Value;
        }
    }

    private ExternalUserIdentity GetExternalIdentity()
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("There is no active HTTP request.");

        var authentication = options.Value;
        return ExternalUserIdentity.Create(
            principal.GetRequiredClaimValue(authentication.ProviderClaimType),
            principal.GetRequiredClaimValue(authentication.SubjectClaimType));
    }
}

internal static class ClaimsPrincipalExtensions
{
    public static string GetRequiredClaimValue(this ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirstValue(claimType);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The authenticated principal does not contain the configured '{claimType}' identity claim.");
        }

        return value;
    }
}
