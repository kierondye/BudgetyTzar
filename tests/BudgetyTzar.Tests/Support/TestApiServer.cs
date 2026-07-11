using BudgetyTzar.Api;
using BudgetyTzar.Api.Features.Identity;
using Microsoft.AspNetCore.Builder;

namespace BudgetyTzar.Tests.Support;

public sealed class TestApiServer : IAsyncDisposable
{
    private TestApiServer(WebApplication app, HttpClient client)
    {
        App = app;
        Client = client;
    }

    public HttpClient Client { get; }

    private WebApplication App { get; }

    public static async Task<TestApiServer> StartAsync()
    {
        return await StartAsync(BudgetyTzarAuthentication.TestScheme);
    }

    public static async Task<TestApiServer> StartWithConfiguredAuthenticationAsync()
    {
        return await StartAsync(BudgetyTzarAuthentication.ConfiguredScheme);
    }

    public HttpClient CreateClientForUser(string userId)
    {
        var client = new HttpClient
        {
            BaseAddress = Client.BaseAddress
        };
        client.DefaultRequestHeaders.Add(BudgetyTzarAuthentication.TestUserHeaderName, userId);

        return client;
    }

    private static async Task<TestApiServer> StartAsync(string authenticationScheme)
    {
        var app = ApiApplication.Create(
        [
            "--urls",
            "http://127.0.0.1:0",
            "--Authentication:DefaultScheme",
            authenticationScheme
        ]);
        await app.StartAsync();

        var address = app.Urls.Single();
        var client = new HttpClient
        {
            BaseAddress = new Uri(address)
        };

        return new TestApiServer(app, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
    }
}
