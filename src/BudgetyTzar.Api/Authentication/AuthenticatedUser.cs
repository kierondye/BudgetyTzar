using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Authentication;

public sealed class AuthenticatedUser(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AuthenticationOptions> options)
{
    public ApplicationUserId UserId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User
                ?? throw new InvalidOperationException("There is no active HTTP request.");

            return ApplicationUserId.FromPrincipal(principal, options.Value.UserIdClaimType);
        }
    }
}

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string Scheme { get; set; } = "Bearer";

    public string UserIdClaimType { get; set; } = "sub";
}
