using BudgetyTzar.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests.Support;

/// <summary>
/// Host-level API test harness that replaces production bearer-token authentication
/// with deterministic test claims. It exercises application authorization and
/// ownership behaviour after authentication, not JWT validation itself.
/// </summary>
public sealed class TestApiServer : IAsyncDisposable
{
    public const string DefaultUserId = "default-test-user";

    private readonly List<HttpClient> clients = [];

    private TestApiServer(WebApplication app)
    {
        App = app;
        Client = CreateClient(DefaultUserId);
    }

    public HttpClient Client { get; }

    private WebApplication App { get; }

    public static async Task<TestApiServer> StartAsync()
    {
        var app = ApiApplication.Create(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Authentication:Scheme"] = TestAuthenticationHandler.SchemeName,
                        ["Authentication:ProviderClaimType"] = "iss",
                        ["Authentication:SubjectClaimType"] = "sub"
                    });
                builder.Services
                    .AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        await app.StartAsync();

        return new TestApiServer(app);
    }

    public HttpClient CreateClient(string? userId, string? provider = "test")
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(App.Urls.Single())
        };

        if (userId is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                TestAuthenticationHandler.UserHeaderName,
                userId);

            if (provider is not null)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    TestAuthenticationHandler.ProviderHeaderName,
                    provider);
            }
        }

        clients.Add(client);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients)
        {
            client.Dispose();
        }

        await App.DisposeAsync();
    }
}
