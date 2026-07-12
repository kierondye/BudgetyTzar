using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Features.Identity;

public static class IdentityBoundary
{
    public const string DefaultScheme = "BudgetyTzarAuthentication";

    public static IServiceCollection AddIdentityBoundary(this IServiceCollection services, IConfiguration configuration)
    {
        var scheme = configuration["Authentication:Scheme"] ?? DefaultScheme;

        services.AddHttpContextAccessor();
        services.AddSingleton<CurrentUserResolver>();
        services.AddSingleton<IAuthorizationHandler, ResolvedCurrentUserAuthorizationHandler>();
        services.AddScoped<ICurrentUser>(services =>
        {
            var context = services.GetRequiredService<IHttpContextAccessor>().HttpContext;
            var resolver = services.GetRequiredService<CurrentUserResolver>();

            return resolver.Resolve(context?.User) is CurrentUserResolution.Authenticated authenticated
                ? authenticated.User
                : throw new InvalidOperationException("The current request is not authenticated.");
        });

        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = scheme;
            options.DefaultChallengeScheme = scheme;
        });

        if (string.Equals(scheme, DefaultScheme, StringComparison.Ordinal))
        {
            authentication.AddScheme<AuthenticationSchemeOptions, RejectingAuthenticationHandler>(
                scheme,
                _ => { });
        }

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(scheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new ResolvedCurrentUserRequirement())
                .Build();
        });

        return services;
    }
}

public sealed record ResolvedCurrentUserRequirement : IAuthorizationRequirement;

public sealed class ResolvedCurrentUserAuthorizationHandler(CurrentUserResolver resolver)
    : AuthorizationHandler<ResolvedCurrentUserRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResolvedCurrentUserRequirement requirement)
    {
        if (resolver.Resolve(context.User) is CurrentUserResolution.Authenticated)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class RejectingAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        return Task.FromResult(AuthenticateResult.NoResult());
    }
}
