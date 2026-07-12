using System.Security.Claims;
using BudgetyTzar.Api.Features.Identity;

namespace BudgetyTzar.Tests.Identity;

public sealed class CurrentUserResolverTests
{
    [Fact]
    public void Resolve_creates_distinct_application_user_ids_for_ambiguous_claim_pairs()
    {
        var resolver = new CurrentUserResolver();

        var firstUser = ResolveAuthenticated(resolver, provider: "a:b", subject: "c");
        var secondUser = ResolveAuthenticated(resolver, provider: "a", subject: "b:c");

        Assert.NotEqual(firstUser.UserId, secondUser.UserId);
    }

    private static CurrentUser ResolveAuthenticated(
        CurrentUserResolver resolver,
        string provider,
        string subject)
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        subject,
                        ClaimValueTypes.String,
                        provider)
                ],
                authenticationType: "test"));

        return resolver.Resolve(principal) is CurrentUserResolution.Authenticated authenticated
            ? authenticated.User
            : throw new InvalidOperationException("Expected authenticated test user.");
    }
}
