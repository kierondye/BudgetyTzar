using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BudgetyTzar.Api.Features.Identity;

public static class IdentityBoundary
{
    public const string DefaultScheme = "BudgetyTzarAuthentication";
    public const string BearerSecuritySchemeName = "Bearer";

    public static IServiceCollection AddIdentityBoundary(this IServiceCollection services, IConfiguration configuration)
    {
        var bearerOptions = BearerAuthenticationOptions.FromConfiguration(
            configuration.GetSection("Authentication:Bearer"));
        bearerOptions.Validate();
        var scheme = configuration["Authentication:Scheme"]
            ?? (bearerOptions.Enabled ? JwtBearerDefaults.AuthenticationScheme : DefaultScheme);

        services.AddHttpContextAccessor();
        services.AddSingleton<IApplicationUserStore, InMemoryApplicationUserStore>();
        services.Configure<CurrentUserResolverOptions>(options =>
        {
            options.UserIdClaimTypes = bearerOptions.UserIdClaim is null
                ? CurrentUserResolverOptions.DefaultUserIdClaimTypes
                : [bearerOptions.UserIdClaim];
        });
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
        else if (string.Equals(scheme, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            authentication.AddJwtBearer(scheme, options => ConfigureJwtBearer(options, bearerOptions));
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

    private static void ConfigureJwtBearer(
        JwtBearerOptions options,
        BearerAuthenticationOptions bearerOptions)
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = bearerOptions.RequireHttpsMetadata;

        if (!string.IsNullOrWhiteSpace(bearerOptions.Authority))
        {
            options.Authority = bearerOptions.Authority;
        }

        if (!string.IsNullOrWhiteSpace(bearerOptions.MetadataAddress))
        {
            options.MetadataAddress = bearerOptions.MetadataAddress;
        }

        if (!string.IsNullOrWhiteSpace(bearerOptions.Audience))
        {
            options.Audience = bearerOptions.Audience;
            options.TokenValidationParameters.ValidAudience = bearerOptions.Audience;
        }

        if (!string.IsNullOrWhiteSpace(bearerOptions.Issuer))
        {
            options.TokenValidationParameters.ValidIssuer = bearerOptions.Issuer;
        }

        if (bearerOptions.ValidAudiences.Count > 0)
        {
            options.TokenValidationParameters.ValidAudiences = bearerOptions.ValidAudiences;
        }

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var resolver = context.HttpContext.RequestServices.GetRequiredService<CurrentUserResolver>();

                if (resolver.Resolve(context.Principal) is not CurrentUserResolution.Authenticated)
                {
                    context.Fail("The authenticated token does not contain a configured user identity claim.");
                }

                return Task.CompletedTask;
            }
        };
    }
}

public sealed record BearerAuthenticationOptions
{
    public bool Enabled { get; init; }

    public string? Authority { get; init; }

    public string? MetadataAddress { get; init; }

    public string? Audience { get; init; }

    public string? Issuer { get; init; }

    public IReadOnlyList<string> ValidAudiences { get; init; } = [];

    public string? UserIdClaim { get; init; }

    public bool RequireHttpsMetadata { get; init; } = true;

    public static BearerAuthenticationOptions FromConfiguration(IConfiguration configuration)
    {
        var validAudiences = configuration.GetSection("ValidAudiences")
            .Get<string[]>()
            ?.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray()
            ?? [];

        return new BearerAuthenticationOptions
        {
            Enabled = configuration.GetValue<bool>("Enabled"),
            Authority = NullIfWhiteSpace(configuration["Authority"]),
            MetadataAddress = NullIfWhiteSpace(configuration["MetadataAddress"]),
            Audience = NullIfWhiteSpace(configuration["Audience"]),
            Issuer = NullIfWhiteSpace(configuration["Issuer"]),
            ValidAudiences = validAudiences,
            UserIdClaim = NullIfWhiteSpace(configuration["UserIdClaim"]),
            RequireHttpsMetadata = configuration.GetValue("RequireHttpsMetadata", true)
        };
    }

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Authority)
            && string.IsNullOrWhiteSpace(MetadataAddress))
        {
            errors.Add(
                "Authentication:Bearer requires Authority or MetadataAddress when Enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(Audience) && ValidAudiences.Count == 0)
        {
            errors.Add(
                "Authentication:Bearer requires Audience or ValidAudiences when Enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(UserIdClaim))
        {
            errors.Add(
                "Authentication:Bearer requires UserIdClaim when Enabled is true.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

public sealed class RequireAuthorizationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requiresAuthorization = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        if (!requiresAuthorization)
        {
            return;
        }

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = IdentityBoundary.BearerSecuritySchemeName
                    }
                }
            ] = []
        });
    }
}
