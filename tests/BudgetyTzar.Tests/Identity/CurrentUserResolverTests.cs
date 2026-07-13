using System.Security.Claims;
using BudgetyTzar.Api.Features.Identity;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Tests.Identity;

public sealed class CurrentUserResolverTests
{
    [Fact]
    public void Resolve_uses_external_identity_as_lookup_key_for_an_internal_application_user_id()
    {
        var applicationUserId = ApplicationUserId.New();
        var users = new StubApplicationUserStore(applicationUserId);
        var resolver = new CurrentUserResolver(Options.Create(new CurrentUserResolverOptions()), users);

        var user = ResolveAuthenticated(resolver, provider: "test-provider", subject: "external-subject");

        Assert.Equal(applicationUserId, user.UserId);
        Assert.NotNull(users.RequestedUserKey);
        Assert.NotEqual("external-subject", user.UserId.Value.ToString());
    }

    [Fact]
    public void Resolve_creates_distinct_application_user_ids_for_ambiguous_claim_pairs()
    {
        var resolver = new CurrentUserResolver(
            Options.Create(new CurrentUserResolverOptions()),
            new InMemoryApplicationUserStore());

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

    private sealed class StubApplicationUserStore(ApplicationUserId applicationUserId) : IApplicationUserStore
    {
        public ApplicationUserKey? RequestedUserKey { get; private set; }

        public ApplicationUserId GetOrCreateApplicationUserId(ApplicationUserKey userKey)
        {
            RequestedUserKey = userKey;
            return applicationUserId;
        }
    }
}
