using BudgetyTzar.Api;
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
        var app = ApiApplication.Create(["--urls", "http://127.0.0.1:0"]);
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
