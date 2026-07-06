using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;
using BudgetyTzar.Api;
using Microsoft.AspNetCore.Builder;

namespace BudgetyTzar.Tests;

public sealed partial class ApiBootstrapTests
{
    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", content);
    }

    [Fact]
    public async Task Version_endpoint_reports_runtime_version_metadata()
    {
        await using var server = await TestApiServer.StartAsync();

        var response = await server.Client.GetFromJsonAsync<VersionResponse>("/api/version");

        Assert.NotNull(response);
        Assert.Matches(SemanticVersionPattern(), response.ProductVersion);
        Assert.StartsWith(response.ProductVersion, response.InformationalVersion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Openapi_document_reports_runtime_version_metadata()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var content = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(content);

        Assert.Equal("BudgetyTzar API", document.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.Matches(
            SemanticVersionPattern(),
            document.RootElement.GetProperty("info").GetProperty("version").GetString() ?? string.Empty);
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemanticVersionPattern();

    private sealed record VersionResponse(string ProductVersion, string InformationalVersion);

    private sealed class TestApiServer : IAsyncDisposable
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
}
