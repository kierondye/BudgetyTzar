using System.Reflection;
using Microsoft.OpenApi.Models;

namespace BudgetyTzar.Api;

public static class ApiApplication
{
    public static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var version = RuntimeVersion.Current;

        builder.Services.AddHealthChecks();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "BudgetyTzar API",
                Version = version.ProductVersion
            });
        });

        var app = builder.Build();

        app.UseSwagger();
        app.UseSwaggerUI();

        app.MapHealthChecks("/health");
        app.MapGet("/api/version", () => version)
            .WithName("GetVersion");

        return app;
    }
}

public sealed record RuntimeVersion(string ProductVersion, string InformationalVersion)
{
    public static RuntimeVersion Current { get; } = Create();

    private static RuntimeVersion Create()
    {
        var assembly = typeof(RuntimeVersion).Assembly;
        var informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        var productVersion = informationalVersion.Split('+', 2)[0];

        return new RuntimeVersion(productVersion, informationalVersion);
    }
}
