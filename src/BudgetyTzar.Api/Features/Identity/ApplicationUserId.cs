using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BudgetyTzar.Api.Features.Identity;

public readonly record struct ApplicationUserId
{
    private ApplicationUserId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out ApplicationUserId userId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            userId = default;
            return false;
        }

        userId = new ApplicationUserId(value.Trim());
        return true;
    }

    public override string ToString()
    {
        return Value;
    }
}

public interface ICurrentUser
{
    ApplicationUserId UserId { get; }
}

public sealed record CurrentUser(ApplicationUserId UserId) : ICurrentUser
{
    public static CurrentUser TestDefault { get; } = new(CreateTestUserId());

    private static ApplicationUserId CreateTestUserId()
    {
        return ApplicationUserId.TryCreate("test-user", out var userId)
            ? userId
            : throw new InvalidOperationException("Default test user identity is invalid.");
    }
}

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public ApplicationUserId UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(BudgetyTzarClaimTypes.ApplicationUserId);

            if (!ApplicationUserId.TryCreate(value, out var userId))
            {
                throw new InvalidOperationException("The authenticated user identity is missing.");
            }

            return userId;
        }
    }
}

public static class BudgetyTzarClaimTypes
{
    public const string ApplicationUserId = "budgetytzar:application-user-id";
}

public static class BudgetyTzarAuthenticationDefaults
{
    public const string Scheme = "BudgetyTzar.Header";

    public const string UserHeaderName = "X-BudgetyTzar-User";
}

public sealed class HeaderIdentityAuthenticationOptions : AuthenticationSchemeOptions
{
    public string UserHeaderName { get; set; } = BudgetyTzarAuthenticationDefaults.UserHeaderName;
}

public sealed class HeaderIdentityAuthenticationHandler(
    IOptionsMonitor<HeaderIdentityAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<HeaderIdentityAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.UserHeaderName, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (values.Count != 1
            || !ApplicationUserId.TryCreate(values[0], out var userId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Authenticated user identity is invalid."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.Value),
            new Claim(BudgetyTzarClaimTypes.ApplicationUserId, userId.Value)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddBudgetyTzarIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var defaultScheme = configuration["Authentication:DefaultScheme"]
            ?? BudgetyTzarAuthenticationDefaults.Scheme;
        var userHeaderName = configuration["Authentication:UserHeaderName"]
            ?? BudgetyTzarAuthenticationDefaults.UserHeaderName;

        services
            .AddAuthentication(defaultScheme)
            .AddScheme<HeaderIdentityAuthenticationOptions, HeaderIdentityAuthenticationHandler>(
                defaultScheme,
                options => options.UserHeaderName = userHeaderName);

        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
    }
}
