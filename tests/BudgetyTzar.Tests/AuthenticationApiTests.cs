using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Tests.Support;

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

    private sealed record CreateBudgetRequest(string Name, string Currency);
}

