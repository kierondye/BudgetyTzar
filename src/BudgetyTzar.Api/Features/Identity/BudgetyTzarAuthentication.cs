using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Features.Identity;

public static class BudgetyTzarAuthentication
{
    public const string ConfiguredScheme = "BudgetyTzar.Configured";

    public const string TestScheme = "BudgetyTzar.Test";

    public const string TestUserHeaderName = "X-Test-User";

    public static IServiceCollection AddBudgetyTzarIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        var defaultScheme = configuration["Authentication:DefaultScheme"];

        if (string.IsNullOrWhiteSpace(defaultScheme))
        {
            defaultScheme = ConfiguredScheme;
        }

        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = defaultScheme;
            options.DefaultChallengeScheme = defaultScheme;
        });

        authentication.AddScheme<AuthenticationSchemeOptions, ConfiguredAuthenticationHandler>(
            ConfiguredScheme,
            options => { });

        authentication.AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
            TestScheme,
            options => { });

        services.AddAuthorization();

        return services;
    }

    private static AuthenticationTicket CreateTicket(string scheme, string subject)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim("sub", subject)
        };
        var identity = new ClaimsIdentity(claims, scheme);
        var principal = new ClaimsPrincipal(identity);

        return new AuthenticationTicket(principal, scheme);
    }

    private sealed class ConfiguredAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var authorization = Request.Headers.Authorization.ToString();

            if (string.IsNullOrWhiteSpace(authorization))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            const string prefix = "BudgetyTzar ";
            if (!authorization.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.Fail("Unsupported authentication scheme."));
            }

            var subject = authorization[prefix.Length..];

            return ApplicationUserId.TryCreate(subject, out _)
                ? Task.FromResult(AuthenticateResult.Success(CreateTicket(Scheme.Name, subject)))
                : Task.FromResult(AuthenticateResult.Fail("A stable user identity is required."));
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers.TryGetValue(TestUserHeaderName, out var values)
                ? values.ToString()
                : ApplicationUserId.DefaultTestUser.Value;

            if (string.Equals(subject, "anonymous", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            return ApplicationUserId.TryCreate(subject, out _)
                ? Task.FromResult(AuthenticateResult.Success(CreateTicket(Scheme.Name, subject)))
                : Task.FromResult(AuthenticateResult.Fail("A stable test user identity is required."));
        }
    }
}
