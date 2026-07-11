using BudgetyTzar.Api;
using BudgetyTzar.Api.Features.Identity;
using Microsoft.AspNetCore.Builder;

namespace BudgetyTzar.Tests.Support;

public sealed class TestApiServer : IAsyncDisposable
{
    public const string DefaultUserId = "test-user";

    private readonly List<HttpClient> clients;

    private TestApiServer(WebApplication app, HttpClient client)
    {
        App = app;
        Client = client;
        clients = [client];
    }

    public HttpClient Client { get; }

    private WebApplication App { get; }

    public static async Task<TestApiServer> StartAsync()
    {
        var app = ApiApplication.Create(["--urls", "http://127.0.0.1:0"]);
        await app.StartAsync();

        var address = app.Urls.Single();
        var client = new HttpClient
        {
            BaseAddress = new Uri(address)
        };
        AddAuthenticatedUser(client, DefaultUserId);

        return new TestApiServer(app, client);
    }

    public HttpClient CreateClientForUser(string userId)
    {
        var client = CreateClient();
        AddAuthenticatedUser(client, userId);
        return client;
    }

    public HttpClient CreateUnauthenticatedClient()
    {
        return CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients)
        {
            client.Dispose();
        }

        await App.DisposeAsync();
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = Client.BaseAddress
        };

        clients.Add(client);
        return client;
    }

    private static void AddAuthenticatedUser(HttpClient client, string userId)
    {
        client.DefaultRequestHeaders.Add(BudgetyTzarAuthenticationDefaults.UserHeaderName, userId);
    }
}
