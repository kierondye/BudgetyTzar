using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using BudgetyTzar.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BudgetyTzar.Tests.Support;

public sealed class TestApiServer : IAsyncDisposable
{
    private const string DefaultUserId = "test-user";
    public const string TestJwtAudience = "budgetytzar-tests";
    public const string TestJwtIssuer = "https://identity.tests.budgetytzar";
    public const string TestJwtSigningKey = "BudgetyTzar.Tests.Jwt.Signing.Key.2026";
    private const string TestScheme = "TestAuthentication";
    private const string TestUserHeaderName = "X-Test-User";

    private TestApiServer(WebApplication app, Uri baseAddress, HttpClient client)
    {
        App = app;
        BaseAddress = baseAddress;
        Client = client;
    }

    public HttpClient Client { get; }

    private WebApplication App { get; }

    private Uri BaseAddress { get; }

    public static async Task<TestApiServer> StartAsync()
    {
        var app = ApiApplication.Create(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("Authentication:Scheme", TestScheme)
                ]);
                builder.Services
                    .AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestScheme,
                        _ => { });
            });
        await app.StartAsync();

        var address = app.Urls.Single();
        var baseAddress = new Uri(address);
        var client = CreateAuthenticatedClient(baseAddress, DefaultUserId);

        return new TestApiServer(app, baseAddress, client);
    }

    public static async Task<TestApiServer> StartWithPostgreSqlAsync(string connectionString)
    {
        var app = ApiApplication.Create(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("Authentication:Scheme", TestScheme),
                    new KeyValuePair<string, string?>("Persistence:Provider", "PostgreSql"),
                    new KeyValuePair<string, string?>("ConnectionStrings:BudgetyTzar", connectionString)
                ]);
                builder.Services
                    .AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestScheme,
                        _ => { });
            });
        await app.StartAsync();

        var address = app.Urls.Single();
        var baseAddress = new Uri(address);
        var client = CreateAuthenticatedClient(baseAddress, DefaultUserId);

        return new TestApiServer(app, baseAddress, client);
    }

    public static async Task<TestApiServer> StartWithBearerAuthenticationAsync(string userIdClaim = "sub")
    {
        var app = ApiApplication.Create(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("Authentication:Bearer:Enabled", "true"),
                    new KeyValuePair<string, string?>("Authentication:Bearer:Authority", TestJwtIssuer),
                    new KeyValuePair<string, string?>("Authentication:Bearer:Audience", TestJwtAudience),
                    new KeyValuePair<string, string?>("Authentication:Bearer:Issuer", TestJwtIssuer),
                    new KeyValuePair<string, string?>("Authentication:Bearer:RequireHttpsMetadata", "false"),
                    new KeyValuePair<string, string?>("Authentication:Bearer:UserIdClaim", userIdClaim)
                ]);
                builder.Services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSigningKey));
                        options.Configuration = new OpenIdConnectConfiguration
                        {
                            Issuer = TestJwtIssuer
                        };
                        options.Configuration.SigningKeys.Add(signingKey);
                        options.TokenValidationParameters.IssuerSigningKey = signingKey;
                        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                        options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
                    });
            });
        await app.StartAsync();

        var address = app.Urls.Single();
        var baseAddress = new Uri(address);
        var client = new HttpClient
        {
            BaseAddress = baseAddress
        };

        return new TestApiServer(app, baseAddress, client);
    }

    public HttpClient CreateClientForUser(string userId)
    {
        return CreateAuthenticatedClient(BaseAddress, userId);
    }

    public HttpClient CreateBearerClient(string token)
    {
        var client = new HttpClient
        {
            BaseAddress = BaseAddress
        };
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return client;
    }

    public HttpClient CreateUnauthenticatedClient()
    {
        return new HttpClient
        {
            BaseAddress = BaseAddress
        };
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
    }

    private static HttpClient CreateAuthenticatedClient(Uri baseAddress, string userId)
    {
        var client = new HttpClient
        {
            BaseAddress = baseAddress
        };
        client.DefaultRequestHeaders.Add(TestUserHeaderName, userId);

        return client;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(TestUserHeaderName, out var values))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var userId = values.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    userId.Trim(),
                    ClaimValueTypes.String,
                    "BudgetyTzar.Tests")
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
