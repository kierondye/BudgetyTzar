using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed partial class VersionMetadataTests
{
    [Fact]
    public async Task VersionEndpointExposesProductSemVer()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();

        var response = await client.GetAsync("/version");
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = payload.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ProductVersion.ProductName, root.GetProperty("product").GetString());
        Assert.Equal(ProductVersion.SemanticVersion, root.GetProperty("version").GetString());
        Assert.Equal(ProductVersion.InformationalVersion, root.GetProperty("informationalVersion").GetString());
        Assert.Matches(SemVerRegex(), root.GetProperty("version").GetString()!);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("environment").GetString()));
    }

    [Fact]
    public async Task SwaggerMetadataIncludesProductVersion()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var info = payload.RootElement.GetProperty("info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ProductVersion.ProductName, info.GetProperty("title").GetString());
        Assert.Equal(ProductVersion.SemanticVersion, info.GetProperty("version").GetString());
    }

    [GeneratedRegex("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$")]
    private static partial Regex SemVerRegex();
}
