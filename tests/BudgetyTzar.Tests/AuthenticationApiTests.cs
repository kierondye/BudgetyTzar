using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Tests.Support;
using Microsoft.Extensions.Configuration;

namespace BudgetyTzar.Tests;

public sealed class AuthenticationApiTests
{
    [Theory]
    [InlineData("POST", "/api/budgets")]
    [InlineData("GET", "/api/budgets")]
    [InlineData("GET", "/api/transactions")]
    [InlineData("GET", "/api/budgets/00000000-0000-0000-0000-000000000001/summary")]
    public async Task Business_api_requests_require_authentication(string method, string path)
    {
        await using var server = await TestApiServer.StartAsync();
        using var client = server.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (method == "POST")
        {
            request.Content = JsonContent.Create(new CreateBudgetRequest("UK", "GBP"));
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/api/version")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Operational_endpoints_remain_public(string path)
    {
        await using var server = await TestApiServer.StartAsync();
        using var client = server.CreateUnauthenticatedClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Configured_bearer_authentication_allows_business_requests()
    {
        await using var server = await TestApiServer.StartWithBearerAuthenticationAsync(userIdClaim: "oid");
        using var userA = server.CreateBearerClient(CreateJwt(("oid", "user-a")));
        using var userB = server.CreateBearerClient(CreateJwt(("oid", "user-b")));

        using var firstResponse = await userA.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("UK", "GBP"));
        using var secondResponse = await userB.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("UK", "GBP"));

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Configured_bearer_authentication_rejects_tokens_without_the_configured_user_claim()
    {
        await using var server = await TestApiServer.StartWithBearerAuthenticationAsync(userIdClaim: "oid");
        using var client = server.CreateBearerClient(CreateJwt(("sub", "user-a")));

        using var response = await client.GetAsync("/api/budgets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Configured_bearer_authentication_rejects_invalid_tokens()
    {
        await using var server = await TestApiServer.StartWithBearerAuthenticationAsync();
        using var client = server.CreateBearerClient("not-a-jwt");

        using var response = await client.GetAsync("/api/budgets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Configured_bearer_authentication_requires_an_explicit_user_claim()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateAppWithBearerConfiguration(
            ("Authentication:Bearer:Enabled", "true"),
            ("Authentication:Bearer:Authority", TestApiServer.TestJwtIssuer),
            ("Authentication:Bearer:Audience", TestApiServer.TestJwtAudience)));

        Assert.Contains("Authentication:Bearer requires UserIdClaim", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configured_bearer_authentication_requires_a_metadata_source()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateAppWithBearerConfiguration(
            ("Authentication:Bearer:Enabled", "true"),
            ("Authentication:Bearer:Audience", TestApiServer.TestJwtAudience),
            ("Authentication:Bearer:UserIdClaim", "sub")));

        Assert.Contains(
            "Authentication:Bearer requires Authority or MetadataAddress",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Configured_bearer_authentication_does_not_accept_issuer_as_the_metadata_source()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateAppWithBearerConfiguration(
            ("Authentication:Bearer:Enabled", "true"),
            ("Authentication:Bearer:Issuer", TestApiServer.TestJwtIssuer),
            ("Authentication:Bearer:Audience", TestApiServer.TestJwtAudience),
            ("Authentication:Bearer:UserIdClaim", "sub")));

        Assert.Contains(
            "Authentication:Bearer requires Authority or MetadataAddress",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Configured_bearer_authentication_requires_an_audience()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateAppWithBearerConfiguration(
            ("Authentication:Bearer:Enabled", "true"),
            ("Authentication:Bearer:Authority", TestApiServer.TestJwtIssuer),
            ("Authentication:Bearer:UserIdClaim", "sub")));

        Assert.Contains("Authentication:Bearer requires Audience or ValidAudiences", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Openapi_documents_bearer_security_for_business_operations()
    {
        await using var server = await TestApiServer.StartAsync();
        using var client = server.CreateUnauthenticatedClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        var document = await response.Content.ReadAsStringAsync();
        using var openApi = JsonDocument.Parse(document);
        var root = openApi.RootElement;
        var securitySchemes = root.GetProperty("components").GetProperty("securitySchemes");
        var budgetsGet = root.GetProperty("paths").GetProperty("/api/budgets").GetProperty("get");
        var versionGet = root.GetProperty("paths").GetProperty("/api/version").GetProperty("get");

        Assert.Contains(securitySchemes.EnumerateObject(), property => property.Name == "Bearer");
        var budgetSecurity = budgetsGet.GetProperty("security");
        Assert.Contains(
            budgetSecurity.EnumerateArray(),
            requirement => requirement.EnumerateObject().Any(property => property.Name == "Bearer"));
        Assert.False(versionGet.TryGetProperty("security", out _));
    }

    private static string CreateJwt(params (string Name, string Value)[] claims)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["iss"] = TestApiServer.TestJwtIssuer,
            ["aud"] = TestApiServer.TestJwtAudience,
            ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds()
        };

        foreach (var claim in claims)
        {
            payload[claim.Name] = claim.Value;
        }

        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        }));
        var body = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var unsignedToken = string.Concat(header, ".", body);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestApiServer.TestJwtSigningKey));
        var signature = Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(unsignedToken)));

        return string.Concat(unsignedToken, ".", signature);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void CreateAppWithBearerConfiguration(params (string Key, string Value)[] configuration)
    {
        ApiApplication.Create(
            ["--urls", "http://127.0.0.1:0"],
            builder => builder.Configuration.AddInMemoryCollection(
                configuration.Select(item => new KeyValuePair<string, string?>(item.Key, item.Value))));
    }

    private sealed record CreateBudgetRequest(string Name, string Currency);
}
